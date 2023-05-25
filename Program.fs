open System
open System.IO
open System.Text.RegularExpressions

open Gsuuon.Command
open Gsuuon.Command.Utility
open Gsuuon.Console.Choose
open Gsuuon.Command.Program.Ffmpeg

let run out err (p: Proc) =
    p <!> Stdout |> consume out
    p <!> Stderr |> consume err
    p |> wait |> ignore

let exec =
#if DEBUG
    run (printf "%s") (eprintf "%s") 
#else
    run ignore ignore
#endif

module ConsoleSetup =
    // Display emojis
    Console.OutputEncoding <- Text.Encoding.UTF8

    let dontDie = ConsoleCancelEventHandler (fun _ e -> e.Cancel <- true)

    let passthroughCtrlC () = Console.CancelKeyPress.AddHandler dontDie

    let interruptOnCtrlC () = Console.CancelKeyPress.RemoveHandler dontDie

type Region = { width: int; height: int }

let lines (x: string) = x.Split "\n"

let dshowDevicesAudio () =
    proc "ffmpeg" "-list_devices true -f dshow -i dummy" |> wait <!> Stderr
    |> readBlock
    |> lines
    |> Array.choose (fun l ->
        let m = Regex.Match(l, """dshow.+] "(?<name>.+)" \((?<type>.+)\)""")

        if m.Success then
            let name = m.Groups["name"].Value
            let typ = m.Groups["type"].Value

            Some {| name = name; ``type`` = typ |}

        else
            None)

type ExecOptions =
    { outputFile: string
      outputDir: string
      videoRegion: Region option
      audioIn: string option
      trimLastSecond: bool }

let doRecord (options: ExecOptions) =
    Directory.CreateDirectory options.outputDir |> ignore

    // We write a temporary recording so we can trim it
    let tmpFilePath = Path.Combine [| options.outputDir; "__raw_last_recording.mp4" |]

    let outputFilePath =
        let path = Path.Combine [| options.outputDir; options.outputFile |]
        // TODO I'm assuming the preset requires mp4 so we do this to avoid
        // having to add mp4 to filename will want to change this if we
        // allow configuring output filetype

        if path.EndsWith ".mp4" then path else path + ".mp4"

    ConsoleSetup.interruptOnCtrlC()

    eprintfn "Recording in 3.."
    sleep 1000
    eprintfn "2.."
    sleep 1000
    eprintfn "1.."
    sleep 1000
    eprintfn "🎬 (ctrl-c to stop)"

    ConsoleSetup.passthroughCtrlC()

    ffmpeg
        [ Nostdin
          Gdigrab
              { framerate = 30
                frame =
                  match options.videoRegion with
                  | Some region ->
                      Region
                          { offsetX = 0
                            offsetY = 0
                            width = region.width
                            height = region.height }
                  | None -> Desktop }

          match options.audioIn with
          | Some source -> Dshow(Audio source)
          | None -> ()

          RawArg Preset.Gdigrab.small ]
        tmpFilePath
    |> exec

    eprintfn "✂️"

    if options.trimLastSecond then
        let fflog = proc "ffmpeg" $"-i {tmpFilePath}" |> wait <!> Stderr |> readBlock

        let matches = Regex.Match(fflog, ".+Duration: ([0-9:.]+)")

        let duration = TimeSpan.Parse(matches.Groups[1].Value)

        let trimMs = 1000
        let endtime = duration - TimeSpan.FromMilliseconds(trimMs)
        eprintfn $"Trimming last {trimMs}ms"

        proc "ffmpeg" $"-i {tmpFilePath} -to {endtime} -y -c copy {outputFilePath}"
        |> exec
    else
        File.Move(tmpFilePath, outputFilePath, true)

    printfn "%s" outputFilePath

let selectVideoRegion () =
    proc "wmic" "path Win32_VideoController get CurrentHorizontalResolution,CurrentVerticalResolution"
    |> wait
    <!> Stdout
    |> readBlock
    |> lines
    |> Array.choose (fun x ->
        let m = Regex.Match(x, "(?<width>\d+)\s+(?<height>\d+)")

        if m.Success then
            Some
                { width = Int32.Parse m.Groups["width"].Value
                  height = Int32.Parse m.Groups["height"].Value }
        else
            None)
    |> Array.toList
    |> choose "Pick a region to capture or esc for entire desktop" 0

let selectAudioIn () =
    dshowDevicesAudio()
    |> Array.choose (fun x -> if x.``type`` = "audio" then Some x.name else None)
    |> Array.toList
    |> choose "Choose audio input (esc or ctrl-c for none):\n" 0

type CLIArg =
    | AudioIn
    | VideoRegion
    | OutputDir of path: string
    | OutputFile of name: string
    | Help
    | NoTrim


let parseSwitches parsed switch =
    let parseSwitch =
        function
        | 'a' -> Some AudioIn
        | 'h' -> Some Help
        | 'v' -> Some VideoRegion
        | 'T' -> Some NoTrim
        | _ -> None

    match parseSwitch switch with
    | Some x -> x :: parsed
    | None ->
        eprintfn "Unknown switch %c" switch
        parsed

let rec parseArgs (args: string list) parsed =
    match args with
    | [] -> parsed
    | "-d" :: path :: rest -> parseArgs rest (OutputDir path :: parsed)
    | "--help" :: rest -> [ Help ]
    | switches :: rest when switches.StartsWith("-") ->
        let parsedSwitches =
            switches.Substring(1)
            |> Seq.fold
                parseSwitches
                parsed

        parseArgs rest parsedSwitches
    | [ name ] -> parseArgs [] (OutputFile name :: parsed)
    | head :: rest ->
        eprintfn $"Ignoring unrecognized option: {head}"
        parseArgs rest parsed

let help =
    """USAGE: grab [options] [<file>]

OUTPUT:
    [<file>]          set output filename (defaults to 'output')

OPTIONS:
    -d <path>         set output directory (defaults to '~/recordings')
    -a                pick an audio source
    -v                pick a video region
    -T                disable trimming last second
"""

let buildOptions args =
    List.fold
        (fun opts arg ->
            match arg with
            | AudioIn -> { opts with audioIn = selectAudioIn () }
            | VideoRegion ->
                { opts with
                    videoRegion = selectVideoRegion () }
            | OutputDir path -> { opts with outputDir = path }
            | OutputFile name -> { opts with outputFile = name }
            | NoTrim -> { opts with trimLastSecond = false }
            | Help ->
                eprintfn "%s" help
                exit 0)
        { outputFile = "output.mp4"
          outputDir =
            Path.Combine
                [| Environment.GetFolderPath Environment.SpecialFolder.UserProfile
                   "recordings" |]
          videoRegion = None
          audioIn = None
          trimLastSecond = true }
        args

let cliArgs = Environment.GetCommandLineArgs() |> Array.toList |> List.tail

ConsoleSetup.passthroughCtrlC ()

parseArgs cliArgs [] |> buildOptions |> doRecord
