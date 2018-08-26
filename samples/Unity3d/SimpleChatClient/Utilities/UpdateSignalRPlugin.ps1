wget https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile nuget.exe
.\nuget.exe install Microsoft.AspNetCore.SignalR.Client -Framework netstandard2.0 -OutputDirectory ".\Package" -Source "https://api.nuget.org/v3/index.json"
mkdir ".\SignalRPlugin"
Get-ChildItem -Path ".\Package" -Filter *.dll -Recurse -File | Where { $_.Directory -like "*lib\netstandard2.0*" } | ForEach-Object { copy $_.FullName ".\SignalRPlugin"}
copy .\SignalRPlugin\ ..\UnityProject\Assets\Plugins\SignalR -Recurse
