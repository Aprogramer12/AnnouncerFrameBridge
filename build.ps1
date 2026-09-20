param([string]$GameRoot=(Join-Path $env:USERPROFILE 'Downloads\LimbusCompany'))
$ErrorActionPreference='Stop'
Add-Type -LiteralPath (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -LiteralPath (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$source=Join-Path $PSScriptRoot 'Plugin.cs'
$output=Join-Path $PSScriptRoot 'AnnouncerFrameBridge.dll'
$refs=[Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$referenceFiles=@(Get-ChildItem -LiteralPath (Join-Path $GameRoot 'dotnet') -Filter '*.dll' | Where-Object { $_.Name -match '^(System\.|mscorlib|netstandard)' -and $_.Name -notmatch '\.Native\.dll$' })
$referenceFiles+=@(Get-ChildItem -LiteralPath (Join-Path $GameRoot 'BepInEx\interop') -Filter '*.dll')
foreach($name in @('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','Il2CppInterop.Runtime.dll','0Harmony.dll')) {
 $referenceFiles+=Get-Item -LiteralPath (Join-Path $GameRoot ('BepInEx\core\'+$name))
}
foreach($file in $referenceFiles) { if($paths.Add($file.FullName)) { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)) } }
$tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source))
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$comp=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('AnnouncerFrameBridge').WithOptions($options).AddSyntaxTrees($tree).AddReferences($refs.ToArray())
$stream=[IO.File]::Create($output)
try { $result=$comp.Emit($stream); $result.Diagnostics | ForEach-Object { $_.ToString() }; if(!$result.Success){throw 'Compilation failed'} } finally { $stream.Dispose() }
Get-FileHash -LiteralPath $output -Algorithm SHA256 | Format-List
