open System
open System.IO
open System.Windows.Forms
open System.Text.RegularExpressions

open Gsuuon.Command
open Gsuuon.Command.Utility
open Gsuuon.Console.Choose
open Gsuuon.Tool.Grab.Ffmpeg

if not (Application.SetHighDpiMode HighDpiMode.PerMonitorV2) then
    eprintfn "Failed to set high dpi mode, video regions may be off"

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

    let dontDie = ConsoleCancelEventHandler(fun _ e -> e.Cancel <- true)

    let passthroughCtrlC () =
        Console.CancelKeyPress.AddHandler dontDie

    let interruptOnCtrlC () =
        Console.CancelKeyPress.RemoveHandler dontDie

type Region = {
    width: int
    height: int
    offsetX: int
    offsetY: int
}

let lines (x: string) = x.Split "\n"

let dshowDevicesAudio () =
    proc "ffmpeg" "-list_devices true -f dshow -i dummy" <!> Stderr
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

[<RequireQualifiedAccess>]
type Format = mp4 | webm

type Options =
    { outputFile: string
      outputDir: string
      outputFormat: Format
      videoRegion: Region option
      audioIn: string option
      audioDelay: float option
      trimLastSecond: bool }

let doRecord (options: Options) =
    Directory.CreateDirectory options.outputDir |> ignore

    // We write a temporary recording so we can trim it
    let tmpFilePath = 
        Path.Combine [|
            options.outputDir
            "__raw_last_recording"
        |] + "." + options.outputFormat.ToString()

    let outputFilePath =
        Path.Combine [|
            options.outputDir
            options.outputFile
        |] + "." + options.outputFormat.ToString()

    ConsoleSetup.interruptOnCtrlC ()

    eprintfn "Recording in 3.."
    sleep 1000
    eprintfn "2.."
    sleep 1000
    eprintfn "1.."
    sleep 1000
    eprintfn "🎬 (ctrl-c to stop)"

    ConsoleSetup.passthroughCtrlC ()

    let small = """-filter_complex "scale=-2:800:flags=lanczos" """
    let mp4 = "-c:v libx264 -preset fast -crf 26 -pix_fmt yuv420p -r 24 -y"
    let webm ="-c:a libvorbis -c:v libvpx-vp9 -preset fast -crf 36 -pix_fmt yuv420p -r 24 -y"

    let formatArgs =
        match options.outputFormat with
        | Format.mp4 -> small + mp4 
        | Format.webm -> small + webm

    ffmpeg
        [ Nostdin
          RawArg "-use_wallclock_as_timestamps 1"
          RawArg "-thread_queue_size 64"
          Gdigrab
              { framerate = 24
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

          match options.audioDelay with
          | Some amt -> RawArg (sprintf "-af asetpts=PTS+%f/TB" amt)
          | None -> ()

          RawArg formatArgs

          RawArg """-movflags +faststart""" ]

        (if options.trimLastSecond then
             tmpFilePath
         else
             outputFilePath)
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

    printfn "%s" outputFilePath

let screenToRegion (screen: Screen) = {
    width = screen.Bounds.Width
    height = screen.Bounds.Height
    offsetX = screen.Bounds.X
    offsetY = screen.Bounds.Y
}
    
module Pick =

    let videoRegion () =
        Screen.AllScreens
        |> Array.map screenToRegion
        |> Array.toList
        |> choose "Pick a region to capture or esc for entire desktop" 0

    let audioIn () =
        dshowDevicesAudio ()
        |> Array.choose (fun x -> if x.``type`` = "audio" then Some x.name else None)
        |> Array.toList
        |> choose "Choose audio input (esc or ctrl-c for none):\n" 0

let help =
    """USAGE: grab [options] [<file>]

OUTPUT:
    [<file>]          set output filename without extension (defaults to 'output')

OPTIONS:
    -d <path>         set output directory (defaults to '~/recordings')
    -a                pick an audio source (no audio without)
    --delay <float>   audio delay
    -v                pick a video region
    -m                output mp4 file (defaults to webm without)
    -T                disable trimming last second
"""

let showHelpAndExit () =
    eprintf "%s" help
    exit 0

let cliArgs = Environment.GetCommandLineArgs() |> Array.toList |> List.tail

let parseOptions args =
    let parseSwitch (options: Options) switch =
        match switch with
        | 'a' -> { options with audioIn = Pick.audioIn () }
        | 'h' -> showHelpAndExit ()
        | 'v' -> { options with videoRegion = Pick.videoRegion () }
        | 'T' -> { options with trimLastSecond = false }
        | 'm' -> { options with outputFormat = Format.mp4 }
        | _ ->
            eprintfn "Unknown switch %c" switch
            options

    let rec parse (args: string list) (options: Options) =
        match args with
        | [] -> options
        | "-d" :: path :: rest -> parse rest { options with outputDir = path }
        | "--delay" :: delay :: rest ->
            let options' =
                try
                    { options with audioDelay = Some(float delay) }
                with
                | _ ->
                    eprintfn "Ignoring failed to parse second: %s" delay
                    options

            parse rest options'
        | "--help" :: rest ->
            showHelpAndExit ()
        | switches :: rest when switches.StartsWith("-") ->
            let parsedSwitches = switches.Substring(1) |> Seq.fold parseSwitch options

            parse rest parsedSwitches
        | [ name ] -> parse [] {
                options with outputFile = name
            }
        | head :: rest ->
            eprintfn $"Ignoring unrecognized option: {head}"
            parse rest options

    parse args
        { outputFile = "output"
          outputFormat = Format.webm
          outputDir =
            Path.Combine
                [| Environment.GetFolderPath Environment.SpecialFolder.UserProfile
                   "recordings" |]
          videoRegion = Some(Screen.PrimaryScreen |> screenToRegion)
          audioIn = None
          audioDelay = None
          trimLastSecond = true }

ConsoleSetup.passthroughCtrlC ()

parseOptions cliArgs |> doRecord
