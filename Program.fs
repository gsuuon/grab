open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions

type ProcResult = {
  out : string
  err : string
  code : int
}

let exec handleOut handleErr (cmd: string) =
  let processStartInfo = new ProcessStartInfo()

  processStartInfo.FileName <- "cmd.exe"
  processStartInfo.Arguments <- sprintf "/C %s" cmd
  processStartInfo.UseShellExecute <- false
  processStartInfo.RedirectStandardOutput <- true
  processStartInfo.RedirectStandardError <- true
  processStartInfo.RedirectStandardInput <- true

  let proc = new Process()
  proc.StartInfo <- processStartInfo
  proc.Start() |> ignore

  let mutable errLines = []
  let mutable outLines = []

  async {
    let stdin = proc.StandardInput

    async {
      use errReader = proc.StandardError

      while not proc.HasExited do
        if not errReader.EndOfStream then
          let err = errReader.ReadLine()
          errLines <- errLines @ [err]
          handleErr err
    } |> Async.Start

    async {
      while not proc.HasExited do
        let line = Console.ReadLine()
        stdin.WriteLine(line)
    } |> Async.Start

    async {
      use reader = proc.StandardOutput

      while not proc.HasExited do
        if not reader.EndOfStream then
          let output = reader.ReadLine()
          outLines <- outLines @ [output]
          handleOut output
    } |> Async.Start
  } |> Async.Start

  Console.CancelKeyPress.Add(fun e ->
    e.Cancel <- true
  )

  proc.WaitForExit() |> ignore

  {
    out = String.concat "\n" outLines
    err = String.concat "\n" errLines
    code = proc.ExitCode
  }

// We're sending everything except output file to stderr so stdout is just the output file name
let show = exec (eprintfn "%s") (eprintfn "%s")
let hide = exec ignore ignore

let sleep ms = Threading.Thread.Sleep 1000

let outputDir =
  Path.Combine [|
    Environment.GetFolderPath Environment.SpecialFolder.UserProfile
    "recordings"
  |]

let outputFilePath =
  let outputFileName =
    let args = Environment.GetCommandLineArgs()

    match Array.tryItem 1 args with
    | Some name ->
      if name.EndsWith "mp4" then name else name + ".mp4"
    | None ->
      "output.mp4"

  Path.Combine [|
    outputDir
    outputFileName
  |]

// We write a temporary recording so we can trim it
let tmpFilePath = Path.Combine [|
  outputDir
  "__raw_last_recording.mp4"
|]

Directory.CreateDirectory outputDir |> ignore

eprintfn "Recording in 3.."
sleep 1000
eprintfn "2.."
sleep 1000
eprintfn "1.."
sleep 1000
eprintfn "Live"

ignore <| hide $"""ffmpeg -f gdigrab -framerate 30 -i desktop -filter_complex "scale=-2:800:flags=lanczos" -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -movflags +faststart -y {tmpFilePath}"""

eprintfn "Stopped"

let res = hide $"""ffmpeg -i {tmpFilePath}"""
let matches = Regex.Match(res.err, ".+Duration: ([0-9:.]+)")

let duration = TimeSpan.Parse(matches.Groups[1].Value)

let trimMs = 400
let endtime = duration - TimeSpan.FromMilliseconds(trimMs)
eprintfn $"Trimming end by {trimMs}ms"

ignore <| hide $"""ffmpeg -i {tmpFilePath} -to {endtime} -y -c copy {outputFilePath}""" 

printfn "%s" outputFilePath
