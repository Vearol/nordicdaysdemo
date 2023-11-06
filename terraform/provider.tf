provider "azurerm" {
  features {}
}

terraform {
  backend "azurerm" {
    resource_group_name  = var.STATE_STORAGE_ACCOUNT_NAME
    storage_account_name = var.STATE_STORAGE_ACCOUNT_NAME
    container_name       = "tfstate"
    key                  = "terraform-base.tfstate"
  }
}