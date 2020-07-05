# Connecting an Azure App Service to Azure PaaS services using Azure Private Link

Azure Private Link allows you to connect privately to Azure PaaS services from an Azure Virtual Network. This eliminates data exposure to the public internet.

In this blog we will look at how we can deploy an ASP.NET Core application to an Azure App Service and connect to an Azure SQL server using Azure Private Link.

We will use the az cli to deploy the ifrastructure and application code. If you want to skip straight to the code. The ARM template is available on github [here](https://github.com/arincoau/app-service-private-link).

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

The virtual network will have two subnets.

- sql (for the Azure SQL Private Endpoint)
  - this subnet will have `privateEndpointNetworkPolicies` disabled. [More info](https://docs.microsoft.com/en-us/azure/private-link/disable-private-endpoint-network-policy)
- web (for the app service)
  - this subnet will be delegated to `Microsoft.Web/serverfarms`

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

### web
``` sh
az network vnet subnet create \
  --resource-group app-service-private-link \
  --vnet-name arinco-app-vnet \
  --name web \
  --address-prefixes 10.0.1.0/24 \
  --delegations Microsoft.Web/serverfarms
```

## A few notes on Private DNS zones

Next up we need to deploy a Private DNS Zone for each of the PaaS services we plan on using. There are a couple of things we should note here regarding DNS resolution of Private Endpoints.

### DNS Resolution of Private Endpoints

When setting up the Private DNS Zones it is good idea to follow the recommended approach of prefixing privatelink to the DNS name of the service. The reasons for this are specified in the Azure documentation and are as follows.

> When you create a private endpoint, the DNS CNAME resource record for the resource is updated to an alias in a subdomain with the prefix 'privatelink'.

> This approach enables access to the resource *using the same connection string* for clients on the VNet hosting the private endpoints, as well as clients outside the VNet.

## Deploy Private DNS Zones

In our application we will be using Azure SQL, therefore we need to deploy a Private DNS zones named `privatelink.database.windows.net`

``` sh
az network private-dns zone create \
  --resource-group app-service-private-link \
  --name privatelink.database.windows.net 
```

And link the Private DNS zones to the VNET we created previously.

``` sh
az network private-dns link vnet create \
  --resource-group app-service-private-link \
  --zone-name  "privatelink.database.windows.net" \
  --name link-to-arinco-app-vnet \
  --virtual-network arinco-app-vnet \
  --registration-enabled false
```

## Deploy an Azure SQL Database

Now we can deploy the SQL server. You should replace the <admin_password> value in the following command with your own password.

``` sh
az sql server create \
  --resource-group app-service-private-link \
  --name arinco-app-sql \
  --admin-user adminuser \
  --admin-password '<admin_password>'
```

And a database

``` sh
az sql db create \
  --resource-group app-service-private-link \
  --server arinco-app-sql \
  --name arinco-app-db \
  --edition Basic
```

## Deploy Azure Private Link - SQL server

We need to create a private endpoint in our vnet for our Azure SQL Server, but before we do this we need to get the resource ID of the sql server we created earlier.

``` sh
az sql server show \
  --resource-group app-service-private-link \
  --name arinco-app-sql \
  --output tsv \
  --query id
```

Take note of the output and replace <sql_server_id> in the following command.

``` sh
az network private-endpoint create \
    --name arinco-app-sql-pe \
    --resource-group app-service-private-link \
    --vnet-name arinco-app-vnet  \
    --subnet sql \
    --private-connection-resource-id <sql_server_id> \
    --group-id sqlServer \
    --connection-name arinco-app-sql-pe-conn
```

Now we need to locate the resource ID of the network interface card that was created by our private link.

``` sh
az network private-endpoint show \
  --name arinco-app-sql-pe \
  --resource-group app-service-private-link \
  --query 'networkInterfaces[0].id' \
  --output tsv
```

Take note of the output and use it as the <nic_id> value in the following command to get the IP address of the private link endpoint. Take note of this value as it will be used to create the a records in the private dns zone.

``` sh
az resource show --ids <nic_id> \
  --api-version 2019-04-01 \
  --query 'properties.ipConfigurations[0].properties.privateIPAddress' \
  --output tsv
```

Now we can create the dns zone entries. Replace <private_ip> with the IP address of the private link we identified above.

``` sh
az network private-dns record-set a create \
  --name arinco-app-sql \
  --zone-name privatelink.database.windows.net \
  --resource-group app-service-private-link

az network private-dns record-set a add-record \
  --record-set-name arinco-app-sql \
  --zone-name privatelink.database.windows.net \
  --resource-group app-service-private-link \
  --ipv4-address <private_ip>
```

## Deploy the App Service

Now we have deployed and configured all the resources required for out web application to function we can now deploy the Web App.

First we deploy the App Service Plan

``` sh
az appservice plan create \
  --resource-group app-service-private-link \
  --name arinco-app-web-asp \
  --sku S1
```

And now we can deploy the Web App

``` sh
az webapp create \
  --resource-group app-service-private-link \
  --plan arinco-app-web-asp \
  --name arinco-app-web 
```

And assign it a managed service identity

``` sh
az webapp identity assign \
  --resource-group app-service-private-link \
  --name arinco-app-web
```

Next we need to link the Web App to the VNET.

```
az webapp vnet-integration add \
  --resource-group app-service-private-link \
  --name arinco-app-web \
  --vnet arinco-app-vnet \
  --subnet web
```

To finish the configuration we need to set a couple of Application settings on the Web App. These configuration setting are WEBSITE_VNET_ROUTE_ALL set to 1 which will tell the App Service to route all traffic via the VNET. By default, your app routes only RFC1918 traffic into your VNET. The other setting is WEBSITE_DNS_SERVER which needs to be set to Azure's virtual public DNS IP address 168.63.129.16. More information on these settings can be found in the Azure documentation [here](https://docs.microsoft.com/en-us/azure/app-service/web-sites-integrate-with-vnet).

``` sh
az webapp config appsettings set \
  --resource-group app-service-private-link \
  --name arinco-app-web \
  --settings WEBSITE_VNET_ROUTE_ALL=1 WEBSITE_DNS_SERVER=168.63.129.16
```

And to connect to our Azure SQL database we need to set the connection string

``` sh
az webapp config connection-string set \
  --resource-group app-service-private-link \
  --name arinco-app-web \
  --connection-string-type SQLAzure \
  --settings DbContext='Server=tcp:arinco-app-sql.database.windows.net,1433;Database=arinco-app-db;'
```

## Validate DNS resolution

We can now validate that resolution of the private endpoints works by opening the portal and navigating to the Web App we created. Then in sidebar under Development Tools select Console.

We can then issue the following command to resolve the database server. Note we are using the address without privatelink. The output address should be the private IP address of the database service private link.

`nameresolver arinco-app-sql.database.windows.net`

``` cmd
D:\home\site\wwwroot>nameresolver arinco-app-sql.database.windows.net
Server: 168.63.129.16

Non-authoritative answer:
Name: arinco-app-sql.privatelink.database.windows.net
Addresses: 
	10.0.0.4
Aliases: 
	arinco-app-sql.privatelink.database.windows.net
```

## Deploy the sample web app

We can now deploy the sample application. This sample application is an ASP.NET Core/Entity Framework Core application. It has an API which returns movies from the database. It is set up to create the database schema on startup and seed the movies table with a few entries.

### SQL Admin

Before we can deploy the sample web app we need to make it an administrator of the Azure SQL Server.

First we need to get the object id of the App Service managed identity. Execute the following and note the output.

``` sh
az ad sp list \
  --display-name arinco-app-web \
  --query '[0].objectId' \
  --output json
```

Then we can add the app service as an administrator of the Azur SQL server. Replate <app_service_object_id> with the output of the command above.

``` sh
az sql server ad-admin create \
  --resource-group app-service-private-link \
  --server-name arinco-app-sql \
  --object-id 5feb9f22-8905-467d-b2fe-1ece38e1d5df \
  --display-name arinco-app-web 
```

### Deploy app code

Now we can deploy the sample application to the app service.

``` sh
az webapp deployment source config \
 --resource-group app-service-private-link \
 --name arinco-app-web \
 --repo-url https://github.com/arincoau/app-service-private-link \
 --branch master \
 --manual-integration
```

And once deployment is complete we can verify everything worked correctly and our website is up and running by browsing: [https://arinco-app-web.azurewebsites.net/api/movies](https://arinco-app-web.azurewebsites.net/api/movies) and it should display the following output:

``` json
[
  {
    "id": 1,
    "title": "The Shawshank Redemption",
    "releaseDate": "1994-10-14T00:00:00",
    "genre": "Drama"
  },
  {
    "id": 2,
    "title": "The Godfather",
    "releaseDate": "1972-03-24T00:00:00",
    "genre": "Drama"
  },
  {
    "id": 3,
    "title": "The Godfather: Part II",
    "releaseDate": "1974-12-18T00:00:00",
    "genre": "Drama"
  },
  {
    "id": 4,
    "title": "The Dark Knight",
    "releaseDate": "2008-07-18T00:00:00",
    "genre": "Action"
  }
]
```