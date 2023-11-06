provider "azurerm" {
  features {}
}

terraform {
  backend "azurerm" {
    resource_group_name  = "nordic-days-demo-terra"
    storage_account_name = "nordicdemostorageterra"
    container_name       = "tfstate"
    key                  = "terraform-base.tfstate"
  }
}