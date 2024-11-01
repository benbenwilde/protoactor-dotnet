Push-Location $PSScriptRoot

echo "publishing"
dotnet publish ./ -c Release -r linux-arm64 --self-contained false
if ($LastExitCode -ne 0)
{
    Write-Error "Error during dotnet build/publish"
    Exit $LastExitCode
}

echo "containerizing"
docker build ./bin/Release/net8.0/linux-arm64/publish --platform linux/arm64 -t "cluster-test:latest"
if ($LastExitCode -ne 0)
{
    Write-Error "Error building docker container"
    Exit $LastExitCode
}

$dirNum = Get-Random -Minimum 1 -Maximum 1000000
echo "dirNum: $dirNum"

echo "deploying"
helm upgrade --install cluster-test ./charts --namespace cluster-test --create-namespace --debug --set image.repository="cluster-test" --set dirNum="$dirNum" 
if ($LastExitCode -ne 0)
{
    Write-Error "Error during helm upgrade"
    Exit $LastExitCode
}
