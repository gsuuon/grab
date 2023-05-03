open System
open System.IO
open System.Text.RegularExpressions

open Gsuuon.Command
open Gsuuon.Command.Utility
open Gsuuon.Console.Choose

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

let outputDir =
    Path.Combine
        [| Environment.GetFolderPath Environment.SpecialFolder.UserProfile
           "recordings" |]

let outputFilePath =
    let outputFileName =
        let args = Environment.GetCommandLineArgs()

        match Array.tryItem 1 args with
        | Some name -> if name.EndsWith "mp4" then name else name + ".mp4"
        | None -> "output.mp4"

    Path.Combine [| outputDir; outputFileName |]

// We write a temporary recording so we can trim it
let tmpFilePath = Path.Combine [| outputDir; "__raw_last_recording.mp4" |]

Directory.CreateDirectory outputDir |> ignore

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

let wmicDisplayResolutions =
    proc "wmic" "path Win32_VideoController get CurrentHorizontalResolution,CurrentVerticalResolution"
    |> wait <!> Stdout |> readBlock |> lines
    |> Array.choose (fun x ->
            let m = Regex.Match(x, "(?<width>\d+)\s+(?<height>\d+)")
            if m.Success then
                Some $"""{m.Groups["width"]}x{m.Groups["height"]}"""
            else
                None
       )
    |> Array.toList

let doRecord videoRegion audioIn =
    eprintfn "Recording in 3.."
    sleep 1000
    eprintfn "2.."
    sleep 1000
    eprintfn "1.."
    sleep 1000
    eprintfn "🎬 (ctrl-c to stop)"

    let args = 
        ( "-f gdigrab -framerate 30 "
        + ( match videoRegion with
            | Some reg -> $"-video_size {reg} -show_region 1 "
            | _ -> ""
          )
        + "-i desktop "
        + ( match audioIn with
            | Some source -> $"""-f dshow -i audio="{source}" """
            | _ -> ""
          )
        + """-filter_complex "scale=-2:800:flags=lanczos" -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -movflags +faststart -y """
        + tmpFilePath
        )

    if debugMode then printfn "ffmpeg args: %s" args

    proc "ffmpeg" args |> exec

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
    wmicDisplayResolutions
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
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | ``Video-Region`` -> "Pick a video region to record"
            | ``Audio-In`` -> "Add an audio source"

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

eprintfn "Region: %A" videoRegion
eprintfn "Audio: %A" audioIn

match choose "Ready to record" 0 [ "Start" ] with
| Some _ ->
    doRecord videoRegion audioIn
| None ->
    eprintfn "Ok, nevermind"
