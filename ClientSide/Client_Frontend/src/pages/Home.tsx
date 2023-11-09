import { AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";
import { Stack } from "@mui/material";
import Button from "@mui/material/Button";
import ButtonGroup from "@mui/material/ButtonGroup";
import Typography from "@mui/material/Typography";
import { MuiFileInput } from "mui-file-input";
import React from "react";
import { Link as RouterLink } from "react-router-dom";
import { msalInstance } from "../index";
import { loginRequest } from "../authConfig";

export function Home() {

  const [value, setValue] = React.useState<File | null>(null);

  const handleChange = (newValue: File | null) => {
    setValue(newValue)
  }

  const clickSubmit = async () => {
    //get token
    const account = msalInstance.getActiveAccount();
    const response = await msalInstance.acquireTokenSilent({
      ...loginRequest,
      account: account
    });

    const token = response.accessToken;

    //call api

    const headers = new Headers();
    const bearer = `Bearer ${token}`;

    headers.append("Authorization", bearer);

    const formData = new FormData();
    formData.append("file", value, value.name);

    const options = {
      method: "POST",
      headers: headers,
      body: formData,
      referrerPolicy: "no-referrer",
      mode: "cors",
    };

    await fetch("https://api/upload", options)
      .then(response => alert(response.statusText))
      .catch(error => console.log(error));
  }


  return (
      <>
          <AuthenticatedTemplate>
            <Stack spacing={2}>
              <MuiFileInput value={value} onChange={handleChange} />
              <Button variant="contained" component="label" onClick={clickSubmit}>Upload</Button>
            </Stack>
          </AuthenticatedTemplate>

          <UnauthenticatedTemplate>
            <Typography variant="h6" align="center">Please sign-in to see your profile information.</Typography>
          </UnauthenticatedTemplate>
      </>
  );
}
