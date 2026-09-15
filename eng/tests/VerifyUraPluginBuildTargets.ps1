param([string]$MSBuildPath = "dotnet")

$ErrorActionPreference = "Stop"

$engRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $engRoot "URA.Plugin.Build.props"
$targetsPath = Join-Path $engRoot "URA.Plugin.Build.targets"
$projectPath = Join-Path (Split-Path -Parent $engRoot) "UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer.csproj"
$errors = New-Object System.Collections.Generic.List[string]

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
if ($project.SelectSingleNode('/Project/PropertyGroup/PackageId').InnerText -ne 'UmamusumeResponseAnalyzer') {
    $errors.Add("$projectPath must pack UmamusumeResponseAnalyzer")
}
if ($project.SelectSingleNode('/Project/PropertyGroup/IncludeBuildOutput').InnerText -ne 'false') {
    $errors.Add("$projectPath must not pack the runtime Host build output")
}
$packedPaths = @($project.SelectNodes('/Project/ItemGroup/None[@Pack="true"]') | ForEach-Object { $_.PackagePath })
foreach ($expectedPath in @('ref\$(TargetFramework)\', 'buildTransitive\UmamusumeResponseAnalyzer.props', 'buildTransitive\UmamusumeResponseAnalyzer.targets')) {
    if ($packedPaths -notcontains $expectedPath) {
        $errors.Add("$projectPath must pack $expectedPath")
    }
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
if (@($props.SelectNodes("/Project/PropertyGroup/DefaultItemExcludes") |
    Where-Object { $_.InnerText.Contains('$(MSBuildProjectDirectory)\deps\**') }).Count -ne 1) {
    $errors.Add("$propsPath must exclude pinned dependency source trees from SDK default items")
}
if (@($props.SelectNodes("/Project/PropertyGroup/DefaultItemExcludes") |
    Where-Object { $_.InnerText.Contains('$(MSBuildProjectDirectory)\tests\**') }).Count -ne 1) {
    $errors.Add("$propsPath must exclude repository-owned test source trees from plugin default items")
}

if (-not (Test-Path -LiteralPath $targetsPath)) {
    $errors.Add("$targetsPath must provide URA plugin build targets")
} else {
    [xml]$targets = Get-Content -LiteralPath $targetsPath -Raw

    if (@($targets.SelectNodes("//UraHostProjectPath")).Count -ne 0) {
        $errors.Add("$targetsPath must not define UraHostProjectPath")
    }

    $expectedDefaultCondition = "'`$(IsUraPlugin)' == ''"
    $defaultPluginValues = @($targets.SelectNodes("/Project/PropertyGroup/IsUraPlugin") |
        Where-Object { $_.Condition -eq $expectedDefaultCondition } |
        ForEach-Object { $_.InnerText.Trim() })
    if ($defaultPluginValues -notcontains "false") {
        $errors.Add("$targetsPath must default IsUraPlugin to false; plugin projects opt in from their csproj")
    }

    if (@($targets.SelectNodes("/Project/ItemGroup/ProjectReference")).Count -ne 0) {
        $errors.Add("$targetsPath must not add Host ProjectReference items")
    }

    if (@($targets.SelectNodes("/Project/Target[@Name='ValidateUraPluginHostProject']")).Count -ne 0) {
        $errors.Add("$targetsPath must not validate a Host source checkout")
    }

    foreach ($targetName in @("GenerateUraPluginManifest", "PackageUraPlugin", "DeployUraPluginToLocalAppData")) {
        if (@($targets.SelectNodes("/Project/Target[@Name='$targetName']")).Count -eq 0) {
            $errors.Add("$targetsPath must define target $targetName")
        }
    }

    if (@($targets.SelectNodes("/Project/UsingTask[@TaskName='WriteUraPluginManifestTask']")).Count -eq 0) {
        $errors.Add("$targetsPath must define WriteUraPluginManifestTask")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 1
}

$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ("ura-manifest-test-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $testProject = @'
<Project>
  <Import Project="__TARGETS__" />
  <Target Name="Verify">
    <WriteUraPluginManifestTask OutputPath="$(MSBuildProjectDirectory)/full.json"
        Author="作者 &quot;A&quot;" InternalName="ManifestSmoke" DisplayName="菜单"
        Description="第一行&#xA;路径 C:\Uma&#x9;结束" Changelog="变更"
        Dependencies=" A, B;C " Targets=" JP;TW " Version="1.2.3.4"
        RepositoryUrl="https://example.com/?a=1&amp;b=2" Category="工具" Homepage="https://example.com/" />
    <WriteUraPluginManifestTask OutputPath="$(MSBuildProjectDirectory)/minimal.json"
        Author="作者" InternalName="MinimalSmoke" DisplayName="最小插件" Version="1.0.0" />
  </Target>
</Project>
'@
    $testProjectPath = Join-Path $testDirectory "manifest.proj"
    [IO.File]::WriteAllText($testProjectPath, $testProject.Replace("__TARGETS__", [Security.SecurityElement]::Escape($targetsPath)))
    $buildArguments = @($testProjectPath, "-nologo", "-t:Verify", "-v:minimal")
    if ([IO.Path]::GetFileNameWithoutExtension($MSBuildPath) -eq "dotnet") {
        $buildArguments = @("msbuild") + $buildArguments
    }
    $startedAt = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    & $MSBuildPath @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Manifest task failed under $MSBuildPath." }

    $full = Get-Content -LiteralPath (Join-Path $testDirectory "full.json") -Raw | ConvertFrom-Json
    $expected = @{
        Author = '作者 "A"'; InternalName = "ManifestSmoke"; DisplayName = "菜单"
        Description = "第一行`n路径 C:\Uma`t结束"; Changelog = "变更"; Version = "1.2.3.4"
        RepositoryUrl = "https://example.com/?a=1&b=2"; Category = "工具"; Homepage = "https://example.com/"
    }
    foreach ($name in $expected.Keys) {
        if ($full.$name -cne $expected[$name]) { throw "Manifest field $name did not round-trip." }
    }
    if (@($full.PSObject.Properties).Count -ne 12 -or
        ($full.Dependencies -join "|") -cne "A|B|C" -or ($full.Targets -join "|") -cne "JP|TW" -or
        $full.LastUpdate -lt $startedAt -or $full.LastUpdate -gt [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) {
        throw "Manifest schema, list values or timestamp are incorrect."
    }
    $minimal = Get-Content -LiteralPath (Join-Path $testDirectory "minimal.json") -Raw | ConvertFrom-Json
    foreach ($name in @("Description", "Changelog", "RepositoryUrl", "Category", "Homepage")) {
        if ($minimal.$name -cne "") { throw "Omitted manifest field $name must be an empty string." }
    }
    if ($minimal.Dependencies -isnot [Array] -or $minimal.Dependencies.Count -ne 0 -or
        $minimal.Targets -isnot [Array] -or $minimal.Targets.Count -ne 0) {
        throw "Omitted manifest lists must be empty arrays."
    }
} finally {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($testDirectory)) -ne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar)) {
        throw "Manifest test cleanup path left the temporary directory."
    }
    Remove-Item -LiteralPath $testDirectory -Recurse -Force
}

Write-Host "URA plugin build targets and manifest serialization passed."
