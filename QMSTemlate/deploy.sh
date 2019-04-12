#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)

usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -n <deploymentName> -l <resourceGroupLocation>" 1>&2; exit 1; }


declare subscriptionId="850e1342-c13f-4483-9312-2e367cefaec8"
declare tenantId="57abfb88-2c2a-4e22-ad26-a2a845843584"
declare appId="49c22ab5-df8d-4627-9575-3ae5de149521"
declare appKey="ypD48oj7ufYquhrPjfgOpeOA9PX6+W86G+SfwMyswXk="

declare resourceGroupName="QuoteMonitoringARM"
declare deploymentName="newdeployment$RANDOM"
declare resourceGroupLocation="southcentralus"
declare templatefile="WebSite.json"
declare pramfile="WebSite.parameters.json"

# Initialize parameters specified from command line
while getopts ":i:g:n:l:t:p:" arg; do
	case "${arg}" in
		i)
			subscriptionId=${OPTARG}
			;;
		g)
			resourceGroupName=${OPTARG}
			;;
		n)
			deploymentName=${OPTARG}
			;;
		l)
			resourceGroupLocation=${OPTARG}
			;;
		t)
			templatefile=${OPTARG}
			;;
		p)
			pramfile=${OPTARG}
			;;
		esac
done
shift $((OPTIND-1))

#Prompt for parameters is some required parameters are missing
if [[ -z "$subscriptionId" ]]; then
	echo "Your subscription ID can be looked up with the CLI using: az account show --out json "
	echo "Enter your subscription ID:"
	read subscriptionId
	[[ "${subscriptionId:?}" ]]
fi

if [[ -z "$resourceGroupName" ]]; then
	echo "This script will look for an existing resource group, otherwise a new one will be created "
	echo "You can create new resource groups with the CLI using: az group create "
	echo "Enter a resource group name"
	read resourceGroupName
	[[ "${resourceGroupName:?}" ]]
fi

if [[ -z "$deploymentName" ]]; then
	echo "Enter a name for this deployment:"
	read deploymentName
fi

if [[ -z "$resourceGroupLocation" ]]; then
	echo "If creating a *new* resource group, you need to set a location "
	echo "You can lookup locations with the CLI using: az account list-locations "
	
	echo "Enter resource group location:"
	read resourceGroupLocation
fi

#templateFile Path - template file to be used

if [ ! -f "$templatefile" ]; then
	echo "$templatefile not found"
	exit 1
fi

#parameter file path

if [ ! -f "$pramfile" ]; then
	echo "$pramfile not found"
	exit 1
fi

if [ -z "$subscriptionId" ] || [ -z "$resourceGroupName" ] || [ -z "$deploymentName" ]; then
	echo "Either one of subscriptionId, resourceGroupName, deploymentName is empty"
	usage
fi

#login to azure using your credentials
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi

#set the default subscription id
az account set --subscription $subscriptionId

set +e

#Check for existing RG
az group show --name $resourceGroupName 1> /dev/null

if [ $? != 0 ]; then
	echo "Resource group with name" $resourceGroupName "could not be found. Creating new resource group.."
	set -e
	(
		set -x
		az group create --name $resourceGroupName --location $resourceGroupLocation 1> /dev/null
	)
	else
	echo "Using existing resource group..."
fi

#Start deployment
#echo "Starting deployment..."
#(
#	set -x
	az group deployment create --name "$deploymentName" --resource-group "$resourceGroupName" --template-file "$templatefile" --parameters "@${pramfile}"
#)

 
#echo "Build Policy Definition..."
#(
	#set -x
	#az policy definition create --display-name "[Temporary] No VM Deployment Allowed in Resource Group" --description "Denied Deployment Policy" --rules "policy_nodeploy.json" --mode All --name "DeniedDeployment" > /dev/null
	#az policy definition create --display-name "[Temporary] Limited VM Size Deployment Allowed in Resource Group" --description "Limited Policy" --rules "policy_vmsize.json" --mode All --name "LimitedDeployment" --params "policy_vmsize_param.json" > /dev/null
#)



echo "update WebSite settings.."
(
	declare queuename="grid-msg"
	declare gridFunctionUrl=""
	declare webappname=""
	webappname="$(az webapp list  --resource-group $resourceGroupName  --query "[0].{name:name}" -o tsv)"

	declare funname=""
	funname="$(az functionapp list  --resource-group $resourceGroupName --query  "[0].{name:name}" -o tsv)"

	echo "--WebAppName:$webappname FunctionName:$funname"

	echo "  publishing function"
	{		
		az functionapp deployment source config-zip  -n $funname -g $resourceGroupName --src "function.zip" #> /dev/null
	}

	echo "  publishing web app"
	{		
		az webapp deployment source config-zip  -n $funname -g $resourceGroupName --src "webapp.zip" #> /dev/null
	}

	echo "  get account connection string"
	(
		declare output="" 
		declare accname=""
		declare accnameKey=""
		accname=$(az storage account list --resource-group  $resourceGroupName  --query "[0].{objectID:name}" -o tsv)
		accnameKey=$(az storage account keys list --account-name $accname --query "[0].{key:value}" -o tsv)
		echo "StorageAccName:$accname StorageAccKey:$accnameKey"
		
		#create queue
		az storage queue create --name $queuename --account-name $accname --account-key $accnameKey > /dev/null

		declare stAccountConnection="DefaultEndpointsProtocol=https;AccountName=$accname;AccountKey=$accnameKey;EndpointSuffix=core.windows.net"
		az functionapp config appsettings set --settings AzureWebJobsStorage="$stAccountConnection" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
		az functionapp config appsettings set --settings StorageConnection="$stAccountConnection" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
		az webapp config appsettings set --settings StorageConnection="$stAccountConnection" --name "$webappname" --resource-group "$resourceGroupName" > /dev/null
		az webapp config appsettings set --settings SubscriptionId="$subscriptionId" --name "$webappname" --resource-group "$resourceGroupName" > /dev/null
		az webapp config appsettings set --settings TenantId="$tenantId" --name "$webappname" --resource-group "$resourceGroupName" > /dev/null
		az webapp config appsettings set --settings AppID="$appId" --name "$webappname" --resource-group "$resourceGroupName" > /dev/null
		az webapp config appsettings set --settings AppKey="$appKey" --name "$webappname" --resource-group "$resourceGroupName" > /dev/null
	)

	echo "  set app settings for function"
	(
		az functionapp config appsettings set --settings FUNCTIONS_EXTENSION_VERSION="~2" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
		az functionapp config appsettings set --settings AzureWebJobsSecretStorageType="Files" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
	)
	
	echo "  set app secrets to webapp"
	(
		az functionapp config appsettings set --settings SubscriptionId="$subscriptionId" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
		az functionapp config appsettings set --settings TenantId="$tenantId" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
		az functionapp config appsettings set --settings AppID="$appId" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
		az functionapp config appsettings set --settings AppKey="$appKey" --name "$funname" --resource-group "$resourceGroupName" > /dev/null
	
		funname=$(az functionapp list  --resource-group $resourceGroupName --query  "[0].{name:name}" -o tsv)
		functionName="GroupUpdateFunction"
		echo "function:$functionName"
		
		user=$(az webapp deployment list-publishing-profiles -n $funname -g $resourceGroupName --query "[?publishMethod=='MSDeploy'].userName" -o tsv)
		pass=$(az webapp deployment list-publishing-profiles -n $funname -g $resourceGroupName --query "[?publishMethod=='MSDeploy'].userPWD" -o tsv)

		echo "user:$user pass:$pass"
		encodedCreds="$(echo -n "$user:$pass" | base64 -w 0 )" 

		jwt=$(curl -X GET -H "Authorization: Basic $encodedCreds" -H "Content-Type: application/json"  https://$funname.scm.azurewebsites.net/api/functions/admin/token)
		jwt=$(echo $jwt | sed "s/\"//g")
		echo "jwt:$jwt"

		keys=$(curl -X GET  -H "Authorization: Bearer $jwt"  https://$funname.azurewebsites.net/admin/functions/$functionName/keys)
		key=$(echo $keys | sed -e "s/.*value\":\"\([^\"]*\)\".*/\1/")
		funRef="https://$funname.azurewebsites.net/api/$functionName/{subID}/{group?}?code=$key"
		echo "webhookref:$funRef"
		#https://quote-mon-fun-alex.azurewebsites.net/api/GroupUpdateFunction/{subID}/{group?}?code=3H6goBwXqq60aWTXEezuKtPO/a6Z5pufIrQ3bjZgIGsUvlaAfdqvBw==
		az webapp config appsettings set --name "$webappname" --resource-group "$resourceGroupName" --settings HttpFunctionWebHook="$funRef" > /dev/null
	
		#generate for data grid
		#functionName="EventGridQuotes"
		#mkey=$(curl -X GET -H "Authorization: Basic $encodedCreds" -H "Content-Type: application/json"  "https://$funname.scm.azurewebsites.net/api/functions/admin/masterkey")
		#mkey=$(echo $mkey | sed -e "s/.*masterKey\":\"\([^\"]*\)\".*/\1/")
		#echo "masterkey:$mkey"
		
		#syskey=$(curl -X GET -H "Content-Type: application/json"  "https://$funname.azurewebsites.net/admin/host/systemkeys/eventgrid_extension?code=$mkey")
		#syskey=$(echo $syskey | sed -e "s/.*value\":\"\([^\"]*\)\".*/\1/")
		#echo "syskey:$syskey"
		
		#gridFunctionUrl="https://$funname.azurewebsites.net/runtime/webhooks/EventGrid?functionName=$functionName&code=$syskey"
		#echo "grid trigged link:'$gridFunctionUrl'"
		#az eventgrid event-subscription create --included-event-types "Microsoft.Resources.ResourceWriteSuccess" "Microsoft.Resources.ResourceDeleteSuccess" "Microsoft.Resources.ResourceActionSuccess"  --endpoint-type webhook --name "event-qms" --endpoint "$gridFunctionUrl"

		#generate for storageacount
		az extension add --name eventgrid
		resoureceid="/subscriptions/$subscriptionId"
		stroageid=$(az storage account list   --resource-group "$resourceGroupName" --query  "[0].{id:id}" -o tsv)
		endpoint="$stroageid/queueservices/default/queues/$queuename"
		echo "endpoint:$endpoint"
		echo "resoureceid:$resoureceid"
		
		az eventgrid event-subscription create --name "event-grid-msg" --included-event-types "Microsoft.Resources.ResourceWriteSuccess" "Microsoft.Resources.ResourceDeleteSuccess" "Microsoft.Resources.ResourceActionSuccess" --endpoint-type storagequeue --source-resource-id   $resoureceid --endpoint $endpoint
	)

	az webapp config appsettings list --name "$webappname" --resource-group "$resourceGroupName"
	az functionapp config appsettings list --name "$funname" --resource-group "$resourceGroupName"
)



if [ $?  == 0 ];
 then
	echo "Template has been successfully deployed"
fi

#read