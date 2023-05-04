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

let hide = run ignore ignore
let show = run (printf "%s") (eprintf "%s")

let debugMode =
    if Environment.GetEnvironmentVariable "DEBUG" = "true" then
        printfn "Debug mode"
        true
    else
        false

let exec = if debugMode then show else hide

setupConsole ()

type Region = {
    width : int
    height : int
}

let lines (x: string) = x.Split "\n"

let dshowDevicesAudio =
    proc "ffmpeg" "-list_devices true -f dshow -i dummy" |> wait <!> Stderr
    |> readBlock
    |> lines
    |> Array.choose (fun l ->
        let m = Regex.Match(l, """dshow.+] "(?<name>.+)" \((?<type>.+)\)""")

        if m.Success then
            let name = m.Groups["name"].Value
            let typ = m.Groups["type"].Value

            Some {|
                name = name
                ``type`` = typ
            |}

        else
            None
    )



let outputDir =
    Path.Combine
        [| Environment.GetFolderPath Environment.SpecialFolder.UserProfile
           "recordings" |]



let doRecord videoRegion audioIn outputDir outputFile =
    Directory.CreateDirectory outputDir |> ignore

    // We write a temporary recording so we can trim it
    let tmpFilePath = Path.Combine [| outputDir; "__raw_last_recording.mp4" |]
    let outputFilePath =
        let path = Path.Combine [| outputDir; outputFile |]
        // TODO I'm assuming the preset requires mp4 so we do this to avoid
        // having to add mp4 to filename will want to change this if we
        // allow configuring output filetype

        if path.EndsWith ".mp4" then path else path + ".mp4"

    eprintfn "Recording in 3.."
    sleep 1000
    eprintfn "2.."
    sleep 1000
    eprintfn "1.."
    sleep 1000
    eprintfn "🎬 (ctrl-c to stop)"

    ffmpeg
        [
            Gdigrab {
                framerate = 30
                frame = 
                    match videoRegion with
                    | Some region -> Region {
                            offsetX = 0
                            offsetY = 0
                            width = region.width
                            height = region.height
                        }
                    | None -> Desktop
            }

            match audioIn with
            | Some source -> Dshow (Audio source)
            | None -> ()

            RawArg Preset.Gdigrab.small
        ]
        tmpFilePath
    |> exec

    eprintfn "✂️"

    let fflog = proc "ffmpeg" $"-i {tmpFilePath}" |> wait <!> Stderr |> readBlock

    let matches = Regex.Match(fflog, ".+Duration: ([0-9:.]+)")

    let duration = TimeSpan.Parse(matches.Groups[1].Value)

    let trimMs = 1000
    let endtime = duration - TimeSpan.FromMilliseconds(trimMs)
    eprintfn $"Trimming last {trimMs}ms"

    proc "ffmpeg" $"-i {tmpFilePath} -to {endtime} -y -c copy {outputFilePath}"
    |> exec

    printfn "%s" outputFilePath

let selectVideoRegion () =
    proc "wmic" "path Win32_VideoController get CurrentHorizontalResolution,CurrentVerticalResolution"
    |> wait <!> Stdout |> readBlock |> lines
    |> Array.choose (fun x ->
            let m = Regex.Match(x, "(?<width>\d+)\s+(?<height>\d+)")
            if m.Success then
                Some {
                    width = Int32.Parse m.Groups["width"].Value
                    height = Int32.Parse m.Groups["height"].Value
                }
            else
                None
       )
    |> Array.toList
    |> choose "Pick a region to capture or esc for entire desktop" 0

let selectAudioIn () =
    dshowDevicesAudio
    |> Array.choose (fun x ->
        if x.``type`` = "audio" then
            Some x.name
        else
            None
    )
    |> Array.toList
    |> choose "Choose audio input (esc or ctrl-c for none):\n" 0

open Argu

type CLIArgs =
    | [<AltCommandLine("-r")>]``Video-Region``
    | [<AltCommandLine("-a")>]``Audio-In``
    | [<AltCommandLine("-d")>]``Output-Dir`` of path: string option
    | [<MainCommand; ExactlyOnce; Last>] ``Output-File`` of filename: string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | ``Video-Region`` -> "Pick a video region to record"
            | ``Audio-In`` -> "Add an audio source"
            | ``Output-Dir`` _ -> "Output directory, defaults to ~/recordings"
            | ``Output-File`` _ -> "Output mp4 filename, defaults to `output`"

let parser = ArgumentParser.Create<CLIArgs>()
let results = parser.ParseCommandLine(raiseOnUsage=false)

if results.IsUsageRequested then printfn "%s" <| parser.PrintUsage() else

let videoRegion =
    if results.Contains ``Video-Region`` then
        selectVideoRegion ()
    else
        None

let audioIn =
    if results.Contains ``Audio-In`` then
        selectAudioIn ()
    else
        None

let outputDir =
    match results.TryGetResult ``Output-Dir`` with
    | Some (Some dir) -> dir
    | _ -> 
        Path.Combine [|
            Environment.GetFolderPath Environment.SpecialFolder.UserProfile
            "recordings"
        |]

let outputFile =
    match results.TryGetResult ``Output-File`` with
    | Some name when name <> "" -> name
    | _ -> "output"

eprintfn "Region: %A" videoRegion
eprintfn "Audio: %A" audioIn

match choose "Ready to record" 0 [ "Start" ] with
| Some _ ->
    doRecord videoRegion audioIn outputDir outputFile
| None ->
    eprintfn "Ok, nevermind"
