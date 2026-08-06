param(
    [Parameter(Mandatory = $true)]
    [string]$DeployDir,

    [string]$GameManaged = 'C:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed',

    [string]$IlRepackPath = (Join-Path $env:USERPROFILE '.dotnet\tools\ilrepack.exe')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $IlRepackPath)) {
    throw "ilrepack not found at $IlRepackPath; install with: dotnet tool install -g dotnet-ilrepack"
}

$mainDll = Join-Path $DeployDir 'CitiesSkylines2Agent.dll'
if (-not (Test-Path -LiteralPath $mainDll)) {
    throw "Main mod dll missing: $mainDll"
}

# The game's mod manager scans every dll in the mod folder and can hit a
# registration-time race when the folder is full of dependency assemblies
# (ExecutableAsset.GetModAssets calls Assembly.Location on every loaded
# assembly and Mono throws for dynamic ones). Keep the folder lean: merge
# all managed dependencies into the single mod dll.
$managedRefs = @(
    'System.Buffers.dll', 'System.Memory.dll', 'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll', 'System.Threading.Tasks.Extensions.dll',
    'System.Text.Encodings.Web.dll', 'System.Text.Json.dll', 'System.Memory.Data.dll',
    'System.Threading.Channels.dll', 'System.IO.Pipelines.dll',
    'System.Net.ServerSentEvents.dll', 'System.Diagnostics.DiagnosticSource.dll',
    'System.Numerics.Tensors.dll', 'Microsoft.Bcl.AsyncInterfaces.dll',
    'Microsoft.Bcl.Numerics.dll', 'Microsoft.Extensions.Primitives.dll',
    'Microsoft.Extensions.Configuration.Abstractions.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
    'Microsoft.Extensions.Diagnostics.Abstractions.dll',
    'Microsoft.Extensions.FileProviders.Abstractions.dll',
    'Microsoft.Extensions.Hosting.Abstractions.dll',
    'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.Options.dll',
    'Microsoft.Extensions.Caching.Abstractions.dll', 'System.ClientModel.dll',
    'Microsoft.Extensions.AI.Abstractions.dll', 'Microsoft.Extensions.AI.dll',
    'Microsoft.Extensions.AI.OpenAI.dll', 'OpenAI.dll'
)

$inputs = @($mainDll)
foreach ($name in $managedRefs) {
    $path = Join-Path $DeployDir $name
    if (Test-Path -LiteralPath $path) {
        $inputs += $path
    }
}

# BCL assemblies the merged dll still references but the game's Managed
# folder does not ship. Pull them from the NuGet cache so the mod stays a
# single self-contained dll.
$extraRefs = @(
    (Join-Path $env:USERPROFILE '.nuget\packages\system.valuetuple\4.6.2\lib\net47\System.ValueTuple.dll'),
    (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\System.ComponentModel.DataAnnotations.dll')
)
foreach ($path in $extraRefs) {
    if (Test-Path -LiteralPath $path) {
        $inputs += $path
    }
    else {
        Write-Warning "Extra merge input not found: $path"
    }
}

if ($inputs.Count -eq 1) {
    # Nothing to merge (already merged).
    exit 0
}

$merged = Join-Path $env:TEMP ("CitiesSkylines2Agent-merged-" + [Guid]::NewGuid().ToString('N') + '.dll')

$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
$args = @(
    ('/out:' + $merged),
    '/target:library',
    '/internalize',
    ('/lib:' + $DeployDir),
    ('/lib:' + $GameManaged)
) + $inputs

& $IlRepackPath $args
if ($LASTEXITCODE -ne 0) {
    throw "ilrepack failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $merged -Destination $mainDll -Force
Remove-Item -LiteralPath $merged -Force

# The merged dll embeds everything; drop the now-redundant dependency dlls and
# the stale pdb so the mod folder stays as small as possible.
foreach ($name in $managedRefs) {
    $path = Join-Path $DeployDir $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}
$pdb = Join-Path $DeployDir 'CitiesSkylines2Agent.pdb'
if (Test-Path -LiteralPath $pdb) {
    Remove-Item -LiteralPath $pdb -Force
}

Write-Output "Merged dependencies into $mainDll"
