data "azurerm_client_config" "current" {
}

# ----------------------- Resource Group ------------------------ 

resource "azurerm_resource_group" "resource_group" {
  name     = "nordic-days-demo-terra2"
  location = "North Europe"
  tags     = { "Env" = "NonProd" }
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

# ----------------------- Cosmos Db ------------------------ 

resource "azurerm_cosmosdb_account" "db_account" {
  name                = "nordicdaysdemo-db-terra2"
  location            = azurerm_resource_group.resource_group.location
  resource_group_name = azurerm_resource_group.resource_group.name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"

  consistency_policy {
    consistency_level       = "Session"
    max_interval_in_seconds = 5
    max_staleness_prefix    = 100
  }

  geo_location {
    location          = azurerm_resource_group.resource_group.location
    failover_priority = 0
  }
}

resource "azurerm_cosmosdb_sql_database" "database" {
  name                = "feedback"
  resource_group_name = azurerm_resource_group.resource_group.name
  account_name        = azurerm_cosmosdb_account.db_account.name
}

resource "azurerm_cosmosdb_sql_container" "db_container" {
  name                  = "reports"
  resource_group_name   = azurerm_resource_group.resource_group.name
  account_name          = azurerm_cosmosdb_account.db_account.name
  database_name         = azurerm_cosmosdb_sql_database.database.name
  partition_key_path    = "/CreationDay"
  partition_key_version = 1
  throughput            = 400

  conflict_resolution_policy {
    mode                     = "LastWriterWins"
    conflict_resolution_path = "/_ts"
  }

  indexing_policy {
    indexing_mode = "consistent"

    included_path {
      path = "/*"
    }

    excluded_path {
      path = "/\"_etag\"/?"
    }
  }
}

# ----------------------- Key Vault ------------------------ 

resource "azurerm_key_vault" "keyvault" {
  name                        = "nordicdaysdemo-kv-terra2"
  location                    = azurerm_resource_group.resource_group.location
  resource_group_name         = azurerm_resource_group.resource_group.name
  enabled_for_disk_encryption = true
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"
}

resource "azurerm_key_vault_access_policy" "functionapp-access-policy" {
  key_vault_id = azurerm_key_vault.keyvault.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = azurerm_linux_function_app.nordicdaysdemo-functionapp-terra.identity[0].principal_id

  secret_permissions = [
    "Get"
  ]
}

resource "azurerm_key_vault_access_policy" "deploy-access-policy" {
  key_vault_id = azurerm_key_vault.keyvault.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = data.azurerm_client_config.current.object_id

  secret_permissions = [
    "Get", "Set", "List"
  ]
}

resource "azurerm_key_vault_secret" "database_key" {
  name         = "database-key"
  value        = azurerm_cosmosdb_account.db_account.primary_sql_connection_string
  key_vault_id = azurerm_key_vault.keyvault.id
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
    "KeyVaultName"                          = "nordicdaysdemo-kv-terra2"
    "ServiceBusConnection"                  = azurerm_servicebus_namespace_authorization_rule.functionapp-listen.primary_connection_string
  }
}