#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)

usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -n <deploymentName> -l <resourceGroupLocation>" 1>&2; exit 1; }

#storage account data
declare acc_subid="850e1342-c13f-4483-9312-2e367cefaec8"
declare resourceGroupName="QuoteMonitoringARM"

#app regestration
declare subscriptionId="850e1342-c13f-4483-9312-2e367cefaec8"
declare tenantId="57abfb88-2c2a-4e22-ad26-a2a845843584"
declare appId="49c22ab5-df8d-4627-9575-3ae5de149521"
declare appKey="ypD48oj7ufYquhrPjfgOpeOA9PX6+W86G+SfwMyswXk="

#target Subscription
declare target_subid="850e1342-c13f-4483-9312-2e367cefaec8"

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

az account set --subscription $acc_subid
 
echo "Build Policy Definition..."
(
	set -x
	#az policy definition create --display-name "[Temporary] No VM Deployment Allowed in Resource Group" --description "Denied Deployment Policy" --rules "policy_nodeploy.json" --mode All --name "DeniedDeployment" 
	#az policy definition create --display-name "[Temporary] Limited VM Size Deployment Allowed in Resource Group" --description "Limited Policy" --rules "policy_vmsize.json" --mode All --name "LimitedDeployment" --params "policy_vmsize_param.json" 
)



echo "update WebSite settings.."
(
	declare queuename="grid-msg"

	echo "  set app secrets to webapp"
	(		
		set -x
		#generate for storageacount
		az extension add --name eventgrid
		resoureceid="/subscriptions/$target_subid"
		stroageid=$(az storage account list  --subscription $acc_subid --resource-group "$resourceGroupName" --query  "[0].{id:id}" -o tsv)
		endpoint="$stroageid/queueservices/default/queues/$queuename"
		echo "endpoint:$endpoint"
		echo "resoureceid:$resoureceid"
		az eventgrid event-subscription create --subscription $target_subid --name "msg-storage" --included-event-types "Microsoft.Resources.ResourceWriteSuccess" "Microsoft.Resources.ResourceDeleteSuccess" "Microsoft.Resources.ResourceActionSuccess" --endpoint-type storagequeue --source-resource-id   $resoureceid --endpoint $endpoint
	)

)



if [ $?  == 0 ];
 then
	echo "Subscription successfully updated"
fi

#read