module Gsuuon.Tool.Grab.Ffmpeg

open Gsuuon.Command

type Dshow =
    | Audio of deviceName: string
    // | Video of deviceName: string

type GdigrabRegion = {
    width : int
    height : int
    offsetX : int
    offsetY : int
}

type GdigrabFrame =
    | Region of GdigrabRegion
    | Window of windowName : string
    | Desktop

type Gdigrab = {
    framerate : int
    frame : GdigrabFrame
}

type FfmpegOption =
    | Dshow of Dshow
    | Gdigrab of Gdigrab
    | RawArg of string
    | Nostdin

module OptToArgstring =
    let private dshowOptionToArg =
        function
        | Audio name -> $"-f dshow -i audio=\"{name}\""

    let private gdigrabOptionToArg  gdigrab =
        match gdigrab.frame with
        | Desktop -> $"-f gdigrab -framerate {gdigrab.framerate} -i desktop"
        | Region region ->
            $"-f gdigrab -framerate {gdigrab.framerate} "
            + $"-offset_x {region.offsetX} -offset_y {region.offsetY} "
            + $"-video_size {region.width}x{region.height} "
            + "-i desktop"
        | Window name ->
            $"-f gdigrab -framerate {gdigrab.framerate} -i title={name}"

    let render =
        function
        | Dshow dshow -> dshowOptionToArg dshow
        | Gdigrab gdigrab -> gdigrabOptionToArg gdigrab
        | RawArg s -> s
        | Nostdin -> "-nostdin" // https://stackoverflow.com/a/47114881

module Dshow =
    open System.Text.RegularExpressions

    let listDevices () =
        proc "ffmpeg" "-list_devices true -f dshow -i dummy" |> wait <!> Stderr
        |> readBlock
        |> fun s -> s.Split "\n"
        |> Seq.choose (fun l ->
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
        |> Seq.toList

module Preset =
    module Gdigrab =
        let twitch ingestUrl twitchKey =
            $"-c:v libx264 -preset veryfast -maxrate 3000k -bufsize 6000k -pix_fmt yuv420p -g 60 -c:a aac -b:a 128k -f flv {ingestUrl}/{twitchKey}"

let ffmpegArgs (options: FfmpegOption list) output =
    let args = options |> List.map OptToArgstring.render |> String.concat " "

    args + " " + output

let ffmpeg (options: FfmpegOption list) output =
    proc "ffmpeg" (ffmpegArgs options output)
