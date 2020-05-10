# Connecting an Azure App Service to Azure PaaS services using Azure Private Link

Azure Private Link allows you to connect privately to Azure PaaS services from an Azure Virtual Network. This eliminates data exposure to the public internet.

In this blog we will look at how we can deploy an Azure App Service and connect it to Azure SQL, Azure Storage and Azure KeyVault using Azure Private Link.

We'll be using the az cli to complete the deployment. If you want to skip straight to the code. The ARM template is available on github [here](https://github.com/arincoau/app-service-private-link).

Or you can deploy it straight to Azure.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Farincoau%2Fapp-service-private-link%2Fmaster%2Fazuredeploy.json)

## Prerequisites

- Azure CLI - [installation instructions](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli?view=azure-cli-latest)

## Before you start

These instructions assumes you are familiar with the Azure CLI and have some general Azure knowledge. The Azure location that resources will be deployed into in this example is Australia East, you can adjust this location to suit your needs, but it will need to be the same for all resources.

## Create Resource Group

Before we start deploying any resources we need to deploy a resource group.

``` sh
az group create \
  --name app-service-private-link \
  --location australiaeast
```

## Deploy the virtual network

To start with we need deploy a virtual network. 

``` sh
az network vnet create \
  --resource-group app-service-private-link \
  --name arinco-app-vnet \
  --location australiaeast
```

The virtual network will have three subnets.

- web (for the app service)
  - this subnet will be delegated to `Microsoft.Web/serverfarms`
- sql (for the Azure SQL Private Endpoint)
  - this subnet will have `privateEndpointNetworkPolicies` disabled. [More info](https://docs.microsoft.com/en-us/azure/private-link/disable-private-endpoint-network-policy)
- storage (for the Azure SQL Private Endpoint)
  - this subnet will have `privateEndpointNetworkPolicies` disabled. [More info](https://docs.microsoft.com/en-us/azure/private-link/disable-private-endpoint-network-policy)

### sql

We need to do this in a two step deployment. One step will create the subnet and the other update the `privateEndpointNetworkPolicies` setting.

``` sh
az network vnet subnet create \
  --resource-group app-service-private-link \
  --vnet-name arinco-app-vnet \
  --name sql \
  --address-prefixes 10.0.0.0/24

az network vnet subnet update \
  --resource-group app-service-private-link \
  --vnet-name arinco-app-vnet \
  --name sql \
  --disable-private-endpoint-network-policies
```

### storage

Same as with the sql subnet we need to perform this deployment in two steps.

``` sh
az network vnet subnet create \
  --resource-group app-service-private-link \
  --vnet-name arinco-app-vnet \
  --name storage \
  --address-prefixes 10.0.1.0/24

az network vnet subnet update \
  --resource-group app-service-private-link \
  --vnet-name arinco-app-vnet \
  --name storage \
  --disable-private-endpoint-network-policies
```

### web
``` sh
az network vnet subnet create \
  --resource-group app-service-private-link \
  --vnet-name arinco-app-vnet \
  --name web \
  --address-prefixes 10.0.2.0/24 \
  --delegations Microsoft.Web/serverfarms
```

## Private DNS zones

Next up we need to deploy a Private DNS Zone for each of the PaaS services we plan on using. There are a couple of things we should note here one regarding DNS resolution of Private Endpoints and the other regarding Azure Storage.

### DNS Resolution of Private Endpoints

When setting up the Private DNS Zones it is good idea to follow the recommended approach of prefixing privatelink to the DNS name of the service. The reasons for this are specified in the Azure documentation and are as follows.

> When you create a private endpoint, the DNS CNAME resource record for the storage account is updated to an alias in a subdomain with the prefix 'privatelink'.

> This approach enables access to the storage account *using the same connection string* for clients on the VNet hosting the private endpoints, as well as clients outside the VNet.

### Azure Storage

 Another thing to note is that Azure Storage is actually made up of a number of different services. We need to deploy a Private DNS Zone for each of those services we plan on using. The list of services and their recommended DNS Zone names are.

 | Storage service        | Zone name                            |
| :--------------------- | :----------------------------------- |
| Blob service           | `privatelink.blob.core.windows.net`  |
| Data Lake Storage Gen2 | `privatelink.dfs.core.windows.net`   |
| File service           | `privatelink.file.core.windows.net`  |
| Queue service          | `privatelink.queue.core.windows.net` |
| Table service          | `privatelink.table.core.windows.net` |
| Static Websites        | `privatelink.web.core.windows.net`   |