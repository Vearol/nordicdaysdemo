terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.79.0"
    }
  }
  backend "azurerm" {
      resource_group_name  = "nordicdays-terra-state3"
      storage_account_name = "nordicdaysterrastate3"
      container_name       = "tfstate"
      key                  = "terraform.tfstate"
      use_oidc             = true
  }

}

provider "azurerm" {
  features {}
  use_oidc = true
}

data "azurerm_client_config" "current" {}

# ----------------------- Resource Group ------------------------ 

resource "azurerm_resource_group" "resource_group" {
  name     = "nordicdays-demo-terra2"
  location = "North Europe"
  tags = { "Env" = "NonProd" }
}

# ----------------------- Storage ------------------------ 

resource "azurerm_storage_account" "storage" {
  name                     = "nordicdemostorageterra2"
  resource_group_name      = azurerm_resource_group.resource_group.name
  location                 = azurerm_resource_group.resource_group.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

# ----------------------- App Service Plan ------------------------ 

resource "azurerm_service_plan" "plan" {
  name                = "nordicdaysdemo-plan-terra2"
  location            = azurerm_resource_group.resource_group.location
  resource_group_name = azurerm_resource_group.resource_group.name
  os_type             = "Linux"
  sku_name            = "S1"
}

# ----------------------- Service Bus ------------------------ 

resource "azurerm_servicebus_namespace" "servicebus" {
  name                = "nordicdaysdemo-sb-terra2"
  location            = azurerm_resource_group.resource_group.location
  resource_group_name = azurerm_resource_group.resource_group.name
  sku                 = "Standard"
}

resource "azurerm_servicebus_queue" "unzip_queue" {
  name         = "blob-unzip"
  namespace_id = azurerm_servicebus_namespace.servicebus.id
}

resource "azurerm_servicebus_queue" "log_analysis_queue" {
  name         = "log-analysis"
  namespace_id = azurerm_servicebus_namespace.servicebus.id
}

resource "azurerm_servicebus_namespace_authorization_rule" "functionapp-listen" {
  name         = "functionapp_auth_rule"
  namespace_id = azurerm_servicebus_namespace.servicebus.id

  listen = true
  send   = false
  manage = false
}

# ----------------------- Application Insights ------------------------ 

resource "azurerm_application_insights" "app-insights" {
  application_type    = "web"
  location            = azurerm_resource_group.resource_group.location
  name                = "nordicdemo-appinsights-terra2"
  resource_group_name = azurerm_resource_group.resource_group.name
}

# ----------------------- Function App ------------------------ 

resource "azurerm_linux_function_app" "nordicdaysdemo-functionapp-terra" {
  resource_group_name = azurerm_resource_group.resource_group.name
  service_plan_id     = azurerm_service_plan.plan.id
  location            = azurerm_resource_group.resource_group.location

  storage_account_name       = azurerm_storage_account.storage.name
  storage_account_access_key = azurerm_storage_account.storage.primary_access_key
  name                       = "nordicdemo-functionapp-terra2"

  site_config {
    application_stack {
      dotnet_version = "6.0"
    }
  }

  identity {
    type = "SystemAssigned"
  }  

  app_settings = {
    "FUNCTIONS_EXTENSION_VERSION"           = "~4"
    "FUNCTIONS_WORKER_RUNTIME"              = "dotnet"
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.app-insights.connection_string
    "AzureWebJobsStorage"                   = azurerm_storage_account.storage.primary_connection_string
    "ServiceBusConnection"                  = azurerm_servicebus_namespace_authorization_rule.functionapp-listen.primary_connection_string
  }
}