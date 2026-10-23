<#
  校验 .snupkg 与对应 .nupkg 是否配套。

  nuget.org 拒收符号包时给的是
    "The uploaded symbols package contains pdb(s) for a corresponding dll(s)
     not found in the nuget package"
  这句只说明"对不上"，不说是哪个 TFM、哪个文件。本脚本把那句话拆开：
  每个 pdb 在包里找同目录同名的 dll，比较 dll 的 CodeView GUID 与 pdb 自身的
  PDB ID，不一致就直接指出是哪一对。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ArtifactsDirectory,
    [Parameter(Mandatory = $true)][string] $PackageId
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction SilentlyContinue

function Expand-Package([string] $Path, [string] $Destination) {
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Path, $Destination)
}

# 调试器用 dll 的 CodeView GUID 去匹配 pdb，所以两边必须是同一个值。
function Get-CodeViewGuid([string] $DllPath) {
    $stream = [System.IO.File]::OpenRead($DllPath)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            foreach ($entry in $pe.ReadDebugDirectory()) {
                if ($entry.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView) {
                    return $pe.ReadCodeViewDebugDirectoryData($entry).Guid
                }
            }
            return $null
        }
        finally { $pe.Dispose() }
    }
    finally { $stream.Dispose() }
}

# portable pdb 的 PDB ID 前 16 字节即为 GUID。
function Get-PdbGuid([string] $PdbPath) {
    $stream = [System.IO.File]::OpenRead($PdbPath)
    try {
        $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
        try {
            $id = $provider.GetMetadataReader().DebugMetadataHeader.Id
            $guidBytes = New-Object byte[] 16
            [System.Array]::Copy($id, 0, $guidBytes, 0, 16)
            return [guid]::new($guidBytes)
        }
        finally { $provider.Dispose() }
    }
    catch {
        return $null
    }
    finally { $stream.Dispose() }
}

$nupkg = Get-ChildItem $ArtifactsDirectory -Filter "$PackageId.*.nupkg" |
    Where-Object { $_.Name -notlike '*.snupkg' } | Select-Object -First 1
$snupkg = Get-ChildItem $ArtifactsDirectory -Filter "$PackageId.*.snupkg" | Select-Object -First 1

if (-not $nupkg) { throw "找不到主包：$ArtifactsDirectory/$PackageId.*.nupkg" }
if (-not $snupkg) {
    Write-Output "没有符号包，跳过：$PackageId"
    return
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-symbols-" + [guid]::NewGuid().ToString('N'))
$mainDir = Join-Path $work 'main'
$symbolDir = Join-Path $work 'symbols'

try {
    Expand-Package $nupkg.FullName $mainDir
    Expand-Package $snupkg.FullName $symbolDir

    $pdbs = @(Get-ChildItem $symbolDir -Recurse -Filter *.pdb)
    if ($pdbs.Count -eq 0) { throw "符号包里没有任何 pdb" }

    # 反向也要查：主包里的每个 dll 都该有对应 pdb，否则符号包是残缺的。
    $dlls = @(Get-ChildItem $mainDir -Recurse -Filter *.dll)
    $problems = @()

    foreach ($pdb in $pdbs) {
        $relative = $pdb.FullName.Substring($symbolDir.Length).TrimStart('\', '/')
        $dllRelative = [System.IO.Path]::ChangeExtension($relative, '.dll')
        $dll = Join-Path $mainDir $dllRelative

        if (-not (Test-Path $dll)) {
            $problems += "$relative → 主包里没有对应的 $dllRelative"
            continue
        }

        $dllGuid = Get-CodeViewGuid $dll
        $pdbGuid = Get-PdbGuid $pdb.FullName

        if ($null -eq $dllGuid) { $problems += "$dllRelative 没有 CodeView 记录" }
        elseif ($null -eq $pdbGuid) { $problems += "$relative 不是可识别的 portable pdb" }
        elseif ($dllGuid -ne $pdbGuid) {
            $problems += "$relative guid=$pdbGuid 与 $dllRelative 的 guid=$dllGuid 不匹配"
        }
        else { Write-Output "  OK    $relative  guid=$pdbGuid" }
    }

    foreach ($dll in $dlls) {
        $relative = $dll.FullName.Substring($mainDir.Length).TrimStart('\', '/')
        $pdbRelative = [System.IO.Path]::ChangeExtension($relative, '.pdb')
        if (-not (Test-Path (Join-Path $symbolDir $pdbRelative))) {
            $problems += "$relative 在符号包里没有对应的 $pdbRelative"
        }
    }

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Output "  失败  $_" }
        throw "符号包与主包不配套（$($problems.Count) 处），推上去会被 nuget.org 拒收。"
    }

    Write-Output "符号包校验通过：$($snupkg.Name)"
}
finally {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}