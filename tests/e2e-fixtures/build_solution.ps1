$ErrorActionPreference = 'Stop'
$scratch = $PSScriptRoot
$solDir = Join-Path $scratch "solution"

[xml]$form = Get-Content (Join-Path $scratch "contact_form.xml") -Raw

# --- 1. add a subgrid section to DETAILS_TAB first column ---
$detailsTab = $form.SelectSingleNode("//tab[@name='DETAILS_TAB']")
$sections = $detailsTab.SelectSingleNode("columns/column/sections")
$subgridSection = $form.CreateDocumentFragment()
$subgridSection.InnerXml = @'
<section name="E2E_SUBGRID_SECTION" showlabel="true" showbar="false" id="{0e2e3001-aaaa-4bbb-8ccc-000000000031}" IsUserDefined="1" layout="varwidth" columns="1" labelwidth="115" celllabelposition="Left"><labels><label description="E2E Subgrid" languagecode="1033" /></labels><rows><row><cell id="{0e2e3002-aaaa-4bbb-8ccc-000000000032}" showlabel="true" rowspan="4" colspan="1" auto="false"><labels><label description="E2E Contacts" languagecode="1033" /></labels><control id="Subgrid_e2e" classid="{E7A81278-8635-4d9e-8D4D-59480B391C5B}" indicationOfSubgrid="true"><parameters><ViewId>{00000000-0000-0000-00AA-000010001004}</ViewId><ViewIds /><TargetEntityType>contact</TargetEntityType><AutoExpand>Fixed</AutoExpand><EnableQuickFind>false</EnableQuickFind><EnableViewPicker>false</EnableViewPicker><EnableJumpBar>false</EnableJumpBar><ChartGridMode>Grid</ChartGridMode><VisualizationId /><IsUserView>false</IsUserView><RelationshipName /><EnableChartPicker>false</EnableChartPicker><RecordsPerPage>4</RecordsPerPage></parameters></control></cell></row></rows></section>
'@
[void]$sections.AppendChild($subgridSection)

# --- 2. events ---
$events = $form.SelectSingleNode("/form/events")

# 2a. UI handlers on the existing form onload event (alongside InternalHandlers)
$onload = $events.SelectSingleNode("event[@name='onload']")
$frag = $form.CreateDocumentFragment()
$frag.InnerXml = @'
<Handlers><Handler functionName="E2ETest.onFormLoad" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1001-aaaa-4bbb-8ccc-000000000011}" enabled="true" parameters="" passExecutionContext="true" /><Handler functionName="E2ETest.onFormLoadExtra" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1002-aaaa-4bbb-8ccc-000000000012}" enabled="true" parameters="&quot;hello&quot;, 42" passExecutionContext="false" /></Handlers>
'@
[void]$onload.AppendChild($frag)

# 2b. onsave, onchange x2, tabstatechange, subgrid onload
$frag = $form.CreateDocumentFragment()
$frag.InnerXml = @'
<event name="onsave" application="false" active="false"><Handlers><Handler functionName="E2ETest.onSave" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1003-aaaa-4bbb-8ccc-000000000013}" enabled="true" parameters="" passExecutionContext="true" /></Handlers></event><event name="onchange" application="false" active="false" attribute="firstname"><Handlers><Handler functionName="E2ETest.onFirstNameChange" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1004-aaaa-4bbb-8ccc-000000000014}" enabled="true" parameters="" passExecutionContext="true" /></Handlers></event><event name="onchange" application="false" active="false" attribute="lastname"><Handlers><Handler functionName="E2ETest.onLastNameChange" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1005-aaaa-4bbb-8ccc-000000000015}" enabled="false" parameters="'test'" passExecutionContext="true" /></Handlers></event><event name="onload" application="false" active="false" control="Subgrid_e2e"><Handlers><Handler functionName="E2ETest.onSubgridLoad" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1007-aaaa-4bbb-8ccc-000000000017}" enabled="true" parameters="" passExecutionContext="true" /></Handlers></event>
'@
[void]$events.AppendChild($frag)

# --- 2c. tabstatechange nested inside DETAILS_TAB ---
$frag = $form.CreateDocumentFragment()
$frag.InnerXml = '<events><event name="tabstatechange" application="false" active="false"><Handlers><Handler functionName="E2ETest.onDetailsTabStateChange" libraryName="e2e_test_handlers.js" handlerUniqueId="{0e2e1006-aaaa-4bbb-8ccc-000000000016}" enabled="true" parameters="" passExecutionContext="false" /></Handlers></event></events>'
[void]$detailsTab.PrependChild($frag)

# --- 3. formLibraries after events ---
$frag = $form.CreateDocumentFragment()
$frag.InnerXml = '<formLibraries><Library name="e2e_test_handlers.js" libraryUniqueId="{0e2e2001-aaaa-4bbb-8ccc-000000000021}" /></formLibraries>'
[void]$form.DocumentElement.InsertAfter($frag, $events)

$formInner = $form.DocumentElement.OuterXml

# --- 4. customizations.xml ---
$customizations = @"
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml version="9.2.0.0" SolutionPackageVersion="9.2" languagecode="1033" generatedBy="CrmLive" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Entities>
    <Entity>
      <Name LocalizedName="Contact" OriginalName="Contact">Contact</Name>
      <FormXml>
        <forms type="main">
          <systemform>
            <formid>{0e2e0001-aaaa-4bbb-8ccc-000000000001}</formid>
            <IntroducedVersion>1.0.0.0</IntroducedVersion>
            <FormPresentation>1</FormPresentation>
            <FormActivationState>1</FormActivationState>
            FORM_PLACEHOLDER
            <IsCustomizable>1</IsCustomizable>
            <CanBeDeleted>1</CanBeDeleted>
            <LocalizedNames>
              <LocalizedName description="E2E Events Test" languagecode="1033" />
            </LocalizedNames>
          </systemform>
        </forms>
      </FormXml>
    </Entity>
  </Entities>
  <Roles />
  <Workflows />
  <FieldSecurityProfiles />
  <Templates />
  <EntityMaps />
  <EntityRelationships />
  <OrganizationSettings />
  <optionsets />
  <CustomControls />
  <EntityDataProviders />
  <WebResources>
    <WebResource>
      <WebResourceId>{0e2e0002-aaaa-4bbb-8ccc-000000000002}</WebResourceId>
      <Name>e2e_test_handlers.js</Name>
      <DisplayName>e2e_test_handlers.js</DisplayName>
      <WebResourceType>3</WebResourceType>
      <IntroducedVersion>1.0.0.0</IntroducedVersion>
      <IsEnabledForMobileClient>0</IsEnabledForMobileClient>
      <FileName>/WebResources/e2e_test_handlers.js</FileName>
    </WebResource>
  </WebResources>
  <Languages>
    <Language>1033</Language>
  </Languages>
</ImportExportXml>
"@

$customizations = $customizations.Replace('FORM_PLACEHOLDER', $formInner)
[xml]$check = $customizations  # validate well-formedness
Set-Content -Path (Join-Path $solDir "customizations.xml") -Value $customizations -NoNewline -Encoding UTF8

# --- 5. zip ---
$zip = Join-Path $scratch "E2EEventsTest.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $solDir "solution.xml"), "solution.xml")
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $solDir "customizations.xml"), "customizations.xml")
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $solDir "[Content_Types].xml"), "[Content_Types].xml")
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $solDir "WebResources\e2e_test_handlers.js"), "WebResources/e2e_test_handlers.js")
} finally { $archive.Dispose() }
"Built: $zip ($((Get-Item $zip).Length) bytes)"
