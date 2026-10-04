param(
    [Parameter(Mandatory = $true)][string]$EpubCheckJar,
    [Parameter(Mandatory = $true)][string]$EpubPath
)
$ErrorActionPreference = 'Stop'
$jar = (Resolve-Path -LiteralPath $EpubCheckJar).Path
$target = Get-Item -LiteralPath $EpubPath
$javaCommand = Get-Command java -ErrorAction Stop
if ($target.PSIsContainer) {
    $books = @(Get-ChildItem -LiteralPath $target.FullName -File -Filter '*.epub')
    $reportParent = $target.FullName
} else {
    if ($target.Extension -ne '.epub') { throw 'Select an EPUB or a folder of EPUBs.' }
    $books = @($target)
    $reportParent = $target.DirectoryName
}
if ($books.Count -eq 0) { throw 'No EPUB files found.' }
$reportFolder = Join-Path $reportParent ('EPUBCheck-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $reportFolder | Out-Null
$failed = $false
foreach ($book in $books) {
    $log = Join-Path $reportFolder ($book.Name + '.log')
    & $javaCommand.Source -jar $jar $book.FullName 2>&1 | Out-File -LiteralPath $log -Encoding utf8
    if ($LASTEXITCODE -ne 0) { $failed = $true }
    Write-Host ($book.Name + ': exit ' + $LASTEXITCODE)
}
Write-Host ('Reports: ' + $reportFolder)
if ($failed) { exit 1 }

