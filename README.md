# 🎞️ grab

Record a video of your desktop with `ffmpeg`

https://user-images.githubusercontent.com/6422188/233815912-65c4a68e-57d0-4f51-8993-7bfb8f068351.mp4

- 👶 easy
- 🍧 peasy


## Use
Outputs mp4 files to `~/recordings`. Optional name for first arg, defaults to `~/recordings/output.mp4`. Make sure `ffmpeg` is available in path and you're on Windows (TODO xplat).

### From repo
```
dotnet run -- my_recording
```

### From nuget
```
dotnet tool install --global Gsuuon.Tool.Grab
grab my_recording
```


