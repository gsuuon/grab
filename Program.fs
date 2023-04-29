open System
open System.IO
open System.Text.RegularExpressions

open Gsuuon.Command
open Gsuuon.Command.Utility

let run out err (p: Proc) =
    p <!> Stdout |> consume out
    p <!> Stderr |> consume err

    p |> wait |> ignore

let hide = run ignore ignore

setupConsole()

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

eprintfn "Recording in 3.."
sleep 1000
eprintfn "2.."
sleep 1000
eprintfn "1.."
sleep 1000
eprintfn "🎬 (ctrl-c to stop)"

proc
    "ffmpeg"
    $"""-f gdigrab -framerate 30 -i desktop -filter_complex "scale=-2:800:flags=lanczos" -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -movflags +faststart -y {tmpFilePath}"""
|> hide

eprintfn "✂️"

let fflog = proc "ffmpeg" $"-i {tmpFilePath}" |> wait <!> Stderr |> readBlock

let matches = Regex.Match(fflog, ".+Duration: ([0-9:.]+)")

let duration = TimeSpan.Parse(matches.Groups[1].Value)

let trimMs = 1000
let endtime = duration - TimeSpan.FromMilliseconds(trimMs)
eprintfn $"Trimming last {trimMs}ms"

proc "ffmpeg" $"-i {tmpFilePath} -to {endtime} -y -c copy {outputFilePath}"
|> hide

printfn "%s" outputFilePath
