param([string]$GameRoot = 'D:\SteamLibrary\steamapps\common\时光餐厅')
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'TimeRestaurant_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('Assembly-CSharp.dll','0Harmony.dll','Newtonsoft.Json.dll','netstandard.dll',
 'UnityEngine.CoreModule.dll','UnityEngine.UI.dll','Unity.TextMeshPro.dll',
 'UnityEngine.UIModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.TextRenderingModule.dll',
 'UnityEngine.IMGUIModule.dll','UnityEngine.ImageConversionModule.dll')
$arguments = @('/nologo','/target:library','/optimize+','/warn:4','/codepage:65001',('/out:' + (Join-Path $packageRoot 'EmployeeCare.dll')))
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $managed $ref) }
$arguments += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output '雇员关怀编译完成。'
