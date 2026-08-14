# Builds the tool and launches a private XrmToolBox instance that contains nothing but it.
#
# The instance lives in tests\.xtb and is created from scratch, so it cannot disturb the
# XrmToolBox you use for real work: its own Plugins folder, its own settings, its own
# connection list. Delete the folder to undo everything this script did.
#
#   .\xtb.ps1              # build, wire up, launch
#   .\xtb.ps1 -Reset       # throw the instance away and rebuild it
#   .\xtb.ps1 -NoLaunch    # set it up without starting XrmToolBox
#
# The connection points at the active organization of the current pac auth profile, which
# is the same environment the e2e fixtures live in. Pass -Environment <url> to aim
# somewhere else.

param(
    [string]$Environment,
    [string]$XrmToolBoxPath = "C:\Users\kk\Downloads\XrmToolbox\XrmToolBox.exe",
    [switch]$Reset,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$instance       = Join-Path $PSScriptRoot ".xtb"
$connectionName = "Events2Code E2E"
$toolName       = "Events2Code"

if (-not (Test-Path $XrmToolBoxPath)) {
    throw "XrmToolBox.exe not found at $XrmToolBoxPath. Pass -XrmToolBoxPath."
}

# Only an XrmToolBox pointed at *this* folder is a problem, because that is the one that
# rewrites these settings on exit. XrmToolBox has no single instance logic of its own, so
# sandboxes belonging to different tools run side by side quite happily, and so does the
# XrmToolBox you work in. Matching on the process name alone would make every project's
# script demand that you close every other project's instance.
$running = Get-CimInstance Win32_Process -Filter "Name='XrmToolBox.exe'" |
           Where-Object { $_.CommandLine -and $_.CommandLine.Contains("/overridepath:$instance") }
if ($running) {
    throw "This instance is already running (pid $($running.ProcessId)). Close that window first: it rewrites its settings on exit and would undo this."
}

if ($Reset -and (Test-Path $instance)) {
    Remove-Item $instance -Recurse -Force
    Write-Host "Removed the previous instance."
}

foreach ($dir in "Plugins", "Settings", "Connections") {
    New-Item -ItemType Directory -Force -Path (Join-Path $instance $dir) | Out-Null
}

# --- the tool ---------------------------------------------------------------------------

$project = Join-Path $PSScriptRoot "..\Events2Code\Events2Code.csproj"
dotnet build $project -c Debug --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $PSScriptRoot "..\Events2Code\bin\Debug\net48\Events2Code.dll"
Copy-Item $dll -Destination (Join-Path $instance "Plugins") -Force

# --- the environment --------------------------------------------------------------------

$who = $null
if ($Environment) {
    $orgUrl = $Environment.TrimEnd('/')
    $unique = ""
} else {
    $who = pac org who --json | ConvertFrom-Json
    if (-not $who.OrgUrl) { throw "pac has no active organization. Run 'pac auth create' or pass -Environment." }
    $orgUrl = $who.OrgUrl.TrimEnd('/')
    $unique = $who.UniqueName
}

$server  = ([Uri]$orgUrl).Host                      # org7a56f694.crm3.dynamics.com
$urlName = $server.Split('.')[0]                    # org7a56f694
$apiHost = $server -replace '^([^.]+)\.', '$1.api.' # org7a56f694.api.crm3.dynamics.com

# The connection signs in the way XrmToolBox's own wizard does: OAuth against Microsoft's
# public Dataverse client id, with the token cached to disk. That means one interactive
# sign in the first time and none after it. The user name is filled in so the sign in
# dialog already knows which account to offer.
$user = if ($who) { $who.UserEmail } else { "" }
$appId = "51f81489-12ee-4a9e-aaae-a2591f45987d"          # the client id XrmToolBox itself uses
$redirect = "app://58145B91-0C36-4500-8554-080854F2AC97" # and its reply url; both must be set or ADAL refuses

$connectionId = "0e2e0001-aaaa-4bbb-8ccc-0000000000e2"  # fixed, so re-running replaces rather than adds
$connections = @"
<?xml version="1.0" encoding="utf-8"?>
<CrmConnections xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <ByPassProxyOnLocal>false</ByPassProxyOnLocal>
  <Connections>
    <ConnectionDetail>
      <AuthType>OnlineFederation</AuthType>
      <AzureAdAppId>$appId</AzureAdAppId>
      <ReplyUrl>$redirect</ReplyUrl>
      <BrowserName>None</BrowserName>
      <ConnectionId>$connectionId</ConnectionId>
      <ConnectionName>$connectionName</ConnectionName>
      <IsCustomAuth>true</IsCustomAuth>
      <IsFromSdkLoginCtrl>false</IsFromSdkLoginCtrl>
      <NewAuthType>OAuth</NewAuthType>
      <Organization>$unique</Organization>
      <OrganizationDataServiceUrl>https://$apiHost/api/data/v9.2/</OrganizationDataServiceUrl>
      <OrganizationFriendlyName>$urlName</OrganizationFriendlyName>
      <OrganizationServiceUrl>https://$apiHost/XRMServices/2011/Organization.svc</OrganizationServiceUrl>
      <OrganizationUrlName>$urlName</OrganizationUrlName>
      <OriginalUrl>$orgUrl/</OriginalUrl>
      <SavePassword>false</SavePassword>
      <ServerName>$server</ServerName>
      <ServerPort>443</ServerPort>
      <Timeout />
      <TimeoutTicks>1200000000</TimeoutTicks>
      <UseIfd>false</UseIfd>
      <UseMfa>true</UseMfa>
      <UserDomain />
      <UserName>$user</UserName>
      <WebApplicationUrl>$orgUrl</WebApplicationUrl>
    </ConnectionDetail>
  </Connections>
  <IsReadOnly>false</IsReadOnly>
  <Name>Default</Name>
  <UseCustomProxy>false</UseCustomProxy>
  <UseDefaultCredentials>false</UseDefaultCredentials>
  <UseDetailsView>false</UseDetailsView>
  <UseInternetExplorerProxy>false</UseInternetExplorerProxy>
  <UseMruDisplay>false</UseMruDisplay>
</CrmConnections>
"@

$connectionsFile = Join-Path $instance "Connections\ConnectionsList.Default.xml"
Set-Content -Path $connectionsFile -Value $connections -Encoding UTF8

$list = @"
<?xml version="1.0" encoding="utf-8"?>
<ConnectionsList xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Files>
    <ConnectionFile>
      <LastUsed>0001-01-01T00:00:00</LastUsed>
      <Name>Default</Name>
      <Path>$connectionsFile</Path>
    </ConnectionFile>
  </Files>
</ConnectionsList>
"@
Set-Content -Path (Join-Path $instance "Connections\MscrmTools.ConnectionsList.xml") -Value $list -Encoding UTF8

# --- settings ---------------------------------------------------------------------------
# Written once. Everything that would put a dialog, a store or an update check between you
# and the tool is turned off; after that XrmToolBox owns the file.

$settingsFile = Join-Path $instance "Settings\XrmToolBox.Settings.xml"
if (-not (Test-Path $settingsFile)) {
    $settings = @"
<?xml version="1.0" encoding="utf-8"?>
<Options xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <BringToTop>true</BringToTop>
  <CheckUpdateOnStartup>false</CheckUpdateOnStartup>
  <DoNotCheckForUpdates>true</DoNotCheckForUpdates>
  <DisplayPluginsStoreOnStartup>false</DisplayPluginsStoreOnStartup>
  <DisplayPluginsStoreOnlyIfUpdates>false</DisplayPluginsStoreOnlyIfUpdates>
  <ShowPluginUpdatesPanelAtStartup>false</ShowPluginUpdatesPanelAtStartup>
  <DoNotShowStartPage>true</DoNotShowStartPage>
  <AllowLogUsage>false</AllowLogUsage>
  <OptinForApplicationInsights>false</OptinForApplicationInsights>
  <CloseEachPluginSilently>true</CloseEachPluginSilently>
  <CloseOpenedPluginsSilently>true</CloseOpenedPluginsSilently>
  <ClosePluginsSilentlyOnWindowsShutdown>true</ClosePluginsSilentlyOnWindowsShutdown>
  <RememberSession>false</RememberSession>
  <DisplayLargeIcons>true</DisplayLargeIcons>
  <DisplayOrder>Alphabetically</DisplayOrder>
  <Theme>Light theme</Theme>
  <LastAdvertisementDisplay>$((Get-Date).ToString("o"))</LastAdvertisementDisplay>
  <LastUpdateCheck>$((Get-Date).ToString("o"))</LastUpdateCheck>
  <LogLevel>Warning</LogLevel>
  <LogRetentionInDays>0</LogRetentionInDays>
  <HiddenPlugins />
  <FormSize>
    <Height>1000</Height>
    <IsMaximized>true</IsMaximized>
    <Width>1600</Width>
  </FormSize>
</Options>
"@
    Set-Content -Path $settingsFile -Value $settings -Encoding UTF8
}

# --- go ---------------------------------------------------------------------------------

Write-Host ""
Write-Host "Instance:   $instance"
Write-Host "Tool:       $toolName  (opens by itself)"
Write-Host "Connection: $connectionName -> $orgUrl"
Write-Host "            signs in interactively the first time, then from the cached token"
Write-Host ""

if ($NoLaunch) { return }

# One string, not an array: XrmToolBox reads /plugin: and /connection: off the raw command
# line, so the quotes have to survive into it. Pass these as separate array elements and it
# hangs on the splash screen forever.
Start-Process $XrmToolBoxPath -ArgumentList "/overridepath:$instance /plugin:`"$toolName`" /connection:`"$connectionName`""
