#Requires -Version 3.0

Param(
    [string] [Parameter(Mandatory=$true)] $ResourceGroupLocation,
    [string] $ResourceGroupName = 'QuoteMonitoringARM',
    [switch] $UploadArtifacts,
    [string] $StorageAccountName,
    [string] $StorageContainerName = $ResourceGroupName.ToLowerInvariant() + '-stageartifacts',
    [string] $TemplateFile = 'WebSite.json',
    [string] $TemplateParametersFile = 'WebSite.parameters.json',
    [string] $ArtifactStagingDirectory = '.',
    [string] $DSCSourceFolder = 'DSC',
    [switch] $ValidateOnly
)


$packageFolder = "C:\Project\_WalMart\QuoteManagmentSystem\Package"

$sub = ((Get-AzureRmContext).Subscription.SubscriptionId)
$currLocation = (Get-Item $MyInvocation.MyCommand.Path)
if ($currLocation -eq $null) {$currLocation=(Get-Location)} else { $currLocation = ($currLocation).DirectoryName }


#Package
Compress-Archive -Path "$packageFolder\Web\*" -DestinationPath "$packageFolder\webapp.zip" -Force
Compress-Archive -Path "$packageFolder\Function\*" -DestinationPath "$packageFolder\function.zip" -Force


Copy-Item -Path "$packageFolder\function.zip" -Destination (join-path $currLocation "function.zip" ) -Force
Copy-Item -Path "$packageFolder\webapp.zip" -Destination (join-path $currLocation "webapp.zip" ) -Force

#deploy

$cli = Join-Path  $currLocation "deploy.sh" 
$param =  "-i '$sub' -g '$ResourceGroupName' -l '$ResourceGroupLocation' -t 'WebSite.json' -p 'WebSite.parameters.json'"
$param = $param.Split(" ")


Write-Host "execute sh with following params '$param'"

&$cli $param 

#Do other stuff here
#Get-Job -Name DoSomething | Wait-Job | Receive-Job

#Write-host "Deployment result:" $result

#Write-Host -NoNewLine 'Press any key to continue...';
#$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown');


#set AzureWebJobsSecretStorageType

$resourceGroupName = "QuotesMonitoring";
$functionAppName = "quote-dev";
$starterFuncName = "EventGridQuotes"; # trigger function (entry point)
#$eventGridSubscriptionName = "MySusbcription";
#$eventGridTopicName = "MyTopic";
#$includedEventTypes = "TextReceived"; # space delimited list of event types

# if not logged in
# az login
# az account set --subscription <sub name>

# if eventgrid extension not installed
# az extension add --name eventgrid

# use Azure CLI to get Kudu creds
Write-Host "Getting Kudu credentials..."
$dep = az webapp deployment list-publishing-profiles -n $functionAppName -g $resourceGroupName --query "[?publishMethod=='MSDeploy']" -o json | ConvertFrom-Json;
$username = $dep.userName;
$pass = $dep.userPWD;

# base 64 creds
$encoded = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("${username}:${pass}"));

# use creds to get master key
Write-Host "Getting master key..."
$masterResp = Invoke-RestMethod -Uri "https://$functionAppName.scm.azurewebsites.net/api/functions/admin/masterkey" -Headers @{"Authorization" = "Basic " + $encoded};

# use master key to get system key
Write-Host "Getting system key..." 
$systemKeyResp = Invoke-RestMethod -Uri "https://$functionAppName.azurewebsites.net/admin/host/systemkeys/eventgrid_extension?code=$($masterResp.masterKey)";

# construct Function URL with system key
$functionUrl = "https://$functionAppName.azurewebsites.net/admin/extensions/EventGridExtensionConfig?functionName=$starterFuncName&code=$($systemKeyResp.value)"

# create Event Grid subscription with Function URL
Write-Host "Creating Event Grid subscription..."
Write-Host ("- for URL: {0}" -f $functionUrl.value)
##az eventgrid event-subscription create -g $resourceGroupName --name $eventGridSubscriptionName --topic-name $eventGridTopicName --endpoint $functionUrl --endpoint-type webhook --included-event-types $includedEventTypes
