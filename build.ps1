# Canonical release build for MailArchiver.
#
# Produces two self-contained, single-file executables at the repo root:
#   MailArchiver-Outlook.exe  — archives Outlook .ost/.pst  -> .msg
#   MailArchiver-Mime.exe     — archives mbox/Maildir/EML   -> .eml
#
# Each csproj has a build target that publishes the shared WinForms viewer
# (framework-dependent, single-file) into src\embedded\ and embeds it as a resource,
# so publishing the archivers is all that is needed — the viewer comes along.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

foreach ($variant in @("Outlook", "Mime")) {
    Write-Host "Publishing MailArchiver-$variant (self-contained single-file)..."
    dotnet publish "$root\src\MailArchiver.$variant\MailArchiver.$variant.csproj" `
        -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o "$root\publish\$variant" --nologo -v q

    Copy-Item "$root\publish\$variant\MailArchiver-$variant.exe" "$root\MailArchiver-$variant.exe" -Force
    $mb = [math]::Round((Get-Item "$root\MailArchiver-$variant.exe").Length / 1MB, 1)
    Write-Host "  -> $root\MailArchiver-$variant.exe ($mb MB)"
}

Write-Host "Done."
