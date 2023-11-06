variable "client_secret" {
}

provider "azurerm" {
  features {}

  client_id       = "80048ea6-2968-4870-9a4e-8f23d163696b"
  client_secret   = var.client_secret
  tenant_id       = "72f988bf-86f1-41af-91ab-2d7cd011db47"
  subscription_id = "fa0c1c72-d987-4fda-a66c-9dcf889f50a9"
}

terraform {
  backend "azurerm" {
    resource_group_name  = "nordic-days-demo-terra"
    storage_account_name = "nordicdemostorageterra"
    container_name       = "tfstate"
    key                  = "terraform-base.tfstate"
  }
}