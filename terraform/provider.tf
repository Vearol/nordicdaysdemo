terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.7.0"
    }
  }
  backend "azurerm" {
      resource_group_name  = "nordicdays-demo-terra2"
      storage_account_name = "nordicdemostorageterra2"
      container_name       = "tfstate"
      key                  = "terraform.tfstate"
      use_oidc             = true
  }

}

provider "azurerm" {
  features {}
  use_oidc = true
}