param([Parameter(Mandatory)][string] $Package)

$ErrorActionPreference = 'Stop'
$packageFile = Get-Item -LiteralPath $Package
$archive = [System.IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
try {
    $entries = $archive.Entries.FullName
    foreach ($name in 'lib/net8.0/iMobileDevice.Managed.dll', 'lib/net8.0/iMobileDevice.Managed.xml', 'iMobileDevice.Managed.nuspec', 'LICENSE.txt', 'README.md') {
        if ($name -cnotin $entries) { throw "Missing package entry: $name" }
    }
    $unexpected = $entries | Where-Object {
        $_ -like 'runtimes/*' -or ($_ -match '\.(dll|so|dylib|exe|py)$' -and $_ -cne 'lib/net8.0/iMobileDevice.Managed.dll')
    }
    if ($unexpected) { throw "Unexpected package payload: $unexpected" }
    $reader = [IO.StreamReader]::new($archive.GetEntry('iMobileDevice.Managed.nuspec').Open())
    try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $nuspec.package.metadata
    if ($metadata.id -cne 'iMobileDevice.Managed' -or $nuspec.SelectNodes("//*[local-name()='dependency']").Count) {
        throw 'Expected a standalone iMobileDevice.Managed package with zero package dependencies.'
    }
} finally {
    $archive.Dispose()
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = [IO.Directory]::CreateTempSubdirectory('imobiledevice-package-')
try {
    $feed = $work.CreateSubdirectory('feed').FullName
    Copy-Item -LiteralPath $packageFile.FullName -Destination $feed
    $project = Join-Path $work.FullName 'Consumer.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="iMobileDevice.Managed" Version="[$($metadata.version)]" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding utf8
    @'
using iMobileDevice.Managed.Plist;
using iMobileDevice.Managed.Usbmuxd;
using iMobileDevice.Managed.Backup;
using iMobileDevice.Managed.Apps;
using iMobileDevice.Managed.Bonjour;
using iMobileDevice.Managed.Afc;

var client = new UsbmuxdClient();
var data = PlistSerializer.Serialize(new PlistString("device"), PlistFormat.Binary);
if (PlistSerializer.Parse(data) is not PlistString { Value: "device" })
    throw new System.Exception("Binary plist round trip failed.");
System.Console.WriteLine(client.GetType().Assembly.FullName);
System.Console.WriteLine($"{typeof(MobileBackupClient).FullName}: {MobileBackupClient.ServiceName}");
await using var apps = new InstallationProxyClient(System.IO.Stream.Null);
await using var afc = new AfcClient(System.IO.Stream.Null);
System.Console.WriteLine($"{AppContainer.ServiceName} via {afc.GetType().Name}, chunk size {new AfcPushOptions().ChunkSize}");
var bonjour = new BonjourClient();
using var stopped = new System.Threading.CancellationTokenSource();
stopped.Cancel();
try
{
    await bonjour.BrowseRemotePairingAsync(cancellationToken: stopped.Token);
    throw new System.Exception("Expected Bonjour to honor caller cancellation.");
}
catch (System.OperationCanceledException ex) when (ex.CancellationToken == stopped.Token) { }
'@ | Set-Content -LiteralPath (Join-Path $work.FullName 'Program.cs') -Encoding utf8

    dotnet restore $project --source $feed --packages (Join-Path $work.FullName 'cache') -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the package from the isolated feed.' }
    dotnet run --project $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Standalone consumer failed.' }
} finally {
    if ($work.Parent.FullName -eq $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -and $work.Name.StartsWith('imobiledevice-package-')) {
        Remove-Item -LiteralPath $work.FullName -Recurse -Force
    }
}
