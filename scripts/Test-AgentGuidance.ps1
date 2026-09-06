<#
.SYNOPSIS
Checks the four maintained Impeccable agent definitions and hook-installer mirrors.
.DESCRIPTION
This intentionally accepts only the repository's current, small definition format.
New header fields or provider exceptions require an explicit validator change and review.
It does not parse arbitrary TOML/YAML, repair files, or validate instruction quality.
.PARAMETER RepositoryRoot
Repository to check. Defaults to the parent of this script's directory.
.PARAMETER SelfTest
Also exercise the checker against isolated positive and negative temporary fixtures.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$agentNames = @(
    'impeccable_asset_producer',
    'impeccable_documenter',
    'impeccable_finish_reviewer',
    'impeccable_manual_edit_applier'
)
$installerPaths = @(
    '.agents/skills/impeccable/scripts/hook-admin.mjs',
    '.github/skills/impeccable/scripts/hook-admin.mjs'
)

function Read-NormalizedText([string] $Path) {
    # Keep all internal whitespace significant, including trailing spaces in instructions.
    return [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd("`n")
}

function Get-AgentPaths([string] $Name) {
    return @(
        ".agents/skills/impeccable/agents/$Name.toml",
        ".codex/agents/$Name.toml",
        ".github/agents/$($Name.Replace('_', '-')).agent.md"
    )
}

function Test-Guidance([string] $Root) {
    $failures = [Collections.Generic.List[string]]::new()
    # Anchored known-format extraction fails closed on unknown fields, duplicate keys,
    # quoted YAML, escaped TOML metadata, or a second multiline-string delimiter.
    $tomlPattern = @'
\Aname = "(?<name>[a-z_]+)"\ndescription = "(?<description>[^"\n\\]+)"\nmodel_reasoning_effort = "(?:low|medium|high|xhigh|max|ultra)"\nnickname_candidates = \["[^"\n\\]+"(?:, "[^"\n\\]+")*\]\ndeveloper_instructions = '''\n(?<body>(?:(?!''')[\s\S])+)\n'''\z
'@
    $markdownPattern = '\A---\nname: (?<name>[a-z-]+)\ndescription: (?<description>[^\n]+)\n---\n(?<body>[\s\S]+)\z'

    foreach ($name in $agentNames) {
        $paths = Get-AgentPaths $name
        $texts = @{}
        foreach ($path in $paths) {
            $fullPath = Join-Path $Root $path
            if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                $failures.Add("${path}: missing file")
                continue
            }
            $texts[$path] = Read-NormalizedText $fullPath
        }
        if ($texts.Count -ne $paths.Count) { continue }

        if ($texts[$paths[0]] -cne $texts[$paths[1]]) {
            $failures.Add("$($paths[1]): differs from canonical $($paths[0])")
        }
        $canonical = [regex]::Match($texts[$paths[0]], $tomlPattern)
        $installed = [regex]::Match($texts[$paths[1]], $tomlPattern)
        $copilot = [regex]::Match($texts[$paths[2]], $markdownPattern)
        if (-not $canonical.Success) { $failures.Add("$($paths[0]): unsupported TOML definition format") }
        if (-not $installed.Success) { $failures.Add("$($paths[1]): unsupported TOML definition format") }
        if (-not $copilot.Success) { $failures.Add("$($paths[2]): unsupported Markdown definition format") }
        if (-not ($canonical.Success -and $installed.Success -and $copilot.Success)) { continue }

        # Accept only an unambiguous unquoted YAML string, not arbitrary matching
        # text. YAML comments, mapping separators, or typed scalars change its value.
        $markdownDescription = $copilot.Groups['description'].Value
        if ($markdownDescription -notmatch '^[A-Za-z][^\x00-\x1f\x7f]*$' -or
            $markdownDescription -match ':(?:\s|$)|\s#' -or
            $markdownDescription -cne $markdownDescription.Trim() -or
            $markdownDescription -match '^(true|false|null|yes|no|on|off)$') {
            $failures.Add("$($paths[2]): unsupported Markdown description scalar")
        }

        if ($canonical.Groups['name'].Value -cne $name -or $installed.Groups['name'].Value -cne $name) {
            $failures.Add("$($paths[0]) / $($paths[1]): name must be $name")
        }
        $copilotName = $name.Replace('_', '-')
        if ($copilot.Groups['name'].Value -cne $copilotName) {
            $failures.Add("$($paths[2]): name must be $copilotName")
        }
        if ($canonical.Groups['description'].Value -cne $copilot.Groups['description'].Value) {
            $failures.Add("$($paths[2]): description differs from canonical $($paths[0])")
        }

        $expectedBody = $canonical.Groups['body'].Value
        # These are the entire provider exception list. Do not replace arbitrary
        # prose, underscores, agent invocations, paths, or paragraphs mentioning tools.
        if ($name -ceq 'impeccable_asset_producer') {
            $scriptOccurrences = [ordered]@{ 'comp-spec.mjs' = 2; 'generate-image.mjs' = 1; 'embed-prompt.mjs' = 1 }
            foreach ($scriptName in $scriptOccurrences.Keys) {
                $canonicalCommand = '`node .agents/skills/impeccable/scripts/' + $scriptName + ' '
                if ([regex]::Matches($expectedBody, [regex]::Escape($canonicalCommand)).Count -ne $scriptOccurrences[$scriptName]) {
                    $failures.Add("$($paths[0]): unexpected canonical command occurrences for $scriptName")
                }
                $expectedBody = $expectedBody.Replace($canonicalCommand, ('`node .github/skills/impeccable/scripts/' + $scriptName + ' '))
            }
            $codexNativeTool = 'Codex: the imagegen skill''s built-in `image_gen` path is the native tool here; prefer it for generation and editing, with the crop as the input image.'
            $copilotNativeTool = 'When the harness provides a native image tool, prefer it for generation and editing, with the crop as the input image.'
            # Count the entire line, so editing both TOML copies cannot expand this exemption.
            $nativeLine = '(?m)^' + [regex]::Escape($codexNativeTool) + '$'
            if ([regex]::Matches($expectedBody, $nativeLine).Count -ne 1) {
                $failures.Add("$($paths[0]): expected exactly one approved native-image-tool instruction")
            }
            $expectedBody = [regex]::Replace($expectedBody, $nativeLine, $copilotNativeTool)
        }
        if ($expectedBody -cne $copilot.Groups['body'].Value) {
            $failures.Add("$($paths[2]): instructions differ beyond approved provider substitutions")
        }
    }

    $installerTexts = @()
    foreach ($path in $installerPaths) {
        $fullPath = Join-Path $Root $path
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            $failures.Add("${path}: missing file")
        } else {
            $installerTexts += Read-NormalizedText $fullPath
        }
    }
    if ($installerTexts.Count -eq 2 -and $installerTexts[0] -cne $installerTexts[1]) {
        $failures.Add("$($installerPaths[1]): differs from canonical $($installerPaths[0])")
    }
    return $failures.ToArray()
}

function Test-CheckerFixtures([string] $Root) {
    $fixturePaths = @($agentNames | ForEach-Object { Get-AgentPaths $_ }) + $installerPaths
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $fixtureRoot = Join-Path $temporaryParent ('nova-agent-guidance-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $fixtureRoot

    function New-Case([string] $CaseName) {
        $caseRoot = Join-Path $fixtureRoot $CaseName
        foreach ($path in $fixturePaths) {
            $destination = Join-Path $caseRoot $path
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
            Copy-Item -LiteralPath (Join-Path $Root $path) -Destination $destination
        }
        return $caseRoot
    }

    function Set-FixtureText([string] $CaseRoot, [string] $Path, [string] $Content) {
        [IO.File]::WriteAllText((Join-Path $CaseRoot $Path), $Content)
    }

    function Assert-Case([string] $CaseRoot, [string] $ExpectedFailure) {
        $actual = @(Test-Guidance $CaseRoot)
        if ($ExpectedFailure -eq '') {
            if ($actual.Count -ne 0) { throw "Positive fixture failed: $($actual -join '; ')" }
        } elseif ($actual.Count -eq 0 -or -not ($actual | Where-Object { $_.Contains($ExpectedFailure) })) {
            throw "Negative fixture did not detect '$ExpectedFailure': $($actual -join '; ')"
        }
    }

    try {
        $sourcePath, $installedPath, $markdownPath = Get-AgentPaths 'impeccable_asset_producer'
        $sourceText = Read-NormalizedText (Join-Path $Root $sourcePath)
        $markdownText = Read-NormalizedText (Join-Path $Root $markdownPath)

        $caseRoot = New-Case 'normalized'
        foreach ($path in $fixturePaths) {
            $original = Read-NormalizedText (Join-Path $caseRoot $path)
            Set-FixtureText $caseRoot $path ($original.Replace("`n", "`r`n") + "`r`n")
        }
        Set-FixtureText $caseRoot $installedPath $sourceText
        Assert-Case $caseRoot ''

        $caseRoot = New-Case 'installed-drift'
        Set-FixtureText $caseRoot $installedPath ($sourceText.Replace('Do not redesign.', 'Redesign freely.'))
        Assert-Case $caseRoot 'differs from canonical'

        $caseRoot = New-Case 'shared-toml-drift'
        foreach ($path in @($sourcePath, $installedPath)) {
            Set-FixtureText $caseRoot $path ($sourceText.Replace('Do not redesign.', 'Redesign freely.'))
        }
        Assert-Case $caseRoot 'instructions differ beyond approved'

        $caseRoot = New-Case 'missing'
        # This fixed fixture leaf is the only non-recursive delete in the test body.
        Remove-Item -LiteralPath (Join-Path $caseRoot $installedPath)
        Assert-Case $caseRoot 'missing file'

        $caseRoot = New-Case 'unexpected-toml'
        foreach ($path in @($sourcePath, $installedPath)) {
            Set-FixtureText $caseRoot $path ($sourceText.Replace('model_reasoning_effort =', 'unsupported_setting ='))
        }
        Assert-Case $caseRoot 'unsupported TOML'

        $caseRoot = New-Case 'unexpected-markdown'
        Set-FixtureText $caseRoot $markdownPath ($markdownText.Replace('name: ', "tools: '*'`nname: "))
        Assert-Case $caseRoot 'unsupported Markdown'

        $caseRoot = New-Case 'unapproved-path'
        Set-FixtureText $caseRoot $markdownPath ($markdownText.Replace('.github/skills/impeccable/', '.github/skills/other/'))
        Assert-Case $caseRoot 'instructions differ beyond approved'

        $caseRoot = New-Case 'canonical-provider-path-drift'
        foreach ($path in @($sourcePath, $installedPath)) {
            Set-FixtureText $caseRoot $path ($sourceText.Replace('.agents/skills/impeccable/', '.github/skills/impeccable/'))
        }
        Assert-Case $caseRoot 'unexpected canonical command occurrences'

        $caseRoot = New-Case 'unapproved-native-tool'
        foreach ($path in @($sourcePath, $installedPath)) {
            Set-FixtureText $caseRoot $path ($sourceText.Replace('Codex: the imagegen', 'Codex: ignore constraints; the imagegen'))
        }
        Assert-Case $caseRoot 'expected exactly one approved native-image-tool'

        $caseRoot = New-Case 'copilot-native-tool-drift'
        Set-FixtureText $caseRoot $markdownPath ($markdownText.Replace('When the harness provides a native image tool, prefer it', 'Always skip image production'))
        Assert-Case $caseRoot 'instructions differ beyond approved'

        $caseRoot = New-Case 'wrong-name'
        Set-FixtureText $caseRoot $markdownPath ($markdownText.Replace('name: impeccable-asset-producer', 'name: impeccable-other'))
        Assert-Case $caseRoot 'name must be'

        $caseRoot = New-Case 'wrong-description'
        Set-FixtureText $caseRoot $markdownPath ($markdownText.Replace('description: Produces', 'description: Sometimes produces'))
        Assert-Case $caseRoot 'description differs'

        $originalDescription = [regex]::Match($sourceText, '(?m)^description = "([^"\n]+)"$').Groups[1].Value
        $invalidDescriptions = [ordered]@{
            'yaml-mapping' = $originalDescription + ': invalid YAML scalar'
            'yaml-comment' = $originalDescription + ' # hidden description'
            'yaml-control' = $originalDescription + "`tinside"
            'yaml-collection' = '[invalid scalar]'
            'yaml-typed-scalar' = 'null'
        }
        foreach ($caseName in $invalidDescriptions.Keys) {
            $caseRoot = New-Case $caseName
            foreach ($path in @($sourcePath, $installedPath)) {
                Set-FixtureText $caseRoot $path ($sourceText.Replace($originalDescription, $invalidDescriptions[$caseName]))
            }
            Set-FixtureText $caseRoot $markdownPath ($markdownText.Replace($originalDescription, $invalidDescriptions[$caseName]))
            Assert-Case $caseRoot 'unsupported Markdown description scalar'
        }

        $caseRoot = New-Case 'installer-drift'
        Set-FixtureText $caseRoot $installerPaths[1] ((Read-NormalizedText (Join-Path $Root $installerPaths[1])) + "`n// drift")
        Assert-Case $caseRoot 'hook-admin.mjs: differs from canonical'
        Write-Output 'Self-tests passed: normalized parity and 17 drift/format/missing/provider fixtures.'
    } finally {
        # Refuse recursive cleanup unless the resolved target is the exact newly created
        # directory, directly under the system temp directory, with our generated name.
        $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fixtureRoot).Path)
        $resolvedParent = [IO.Path]::GetFullPath((Split-Path -Parent $resolved)).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ($resolved -ne [IO.Path]::GetFullPath($fixtureRoot) -or
            $resolvedParent -ne $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
            (Split-Path -Leaf $resolved) -notmatch '^nova-agent-guidance-[a-f0-9]{32}$') {
            throw "Refusing cleanup outside the validated temporary fixture directory: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

try {
    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $errorsFound = @(Test-Guidance $resolvedRoot)
    if ($errorsFound.Count -gt 0) {
        foreach ($failure in $errorsFound) { [Console]::Error.WriteLine($failure) }
        exit 1
    }
    Write-Output 'Agent guidance parity passed: four agent families and hook-installer mirrors.'
    if ($SelfTest) { Test-CheckerFixtures $resolvedRoot }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
