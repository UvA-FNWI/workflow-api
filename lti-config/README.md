# Canvas LTI config
This folder contains config files for configuring Milestones in Canvas per environment.
The JSON contains all the configuration needed for the LTI key, except for the redirect URI, which should be set to `/signin-oidc` on the API:
- Production (<https://canvas.uva.nl>): TODO
- Acceptance (<https://uvadlo.test.instructure.com>): `https://workflow-api-acc.datanose.nl/signin-oidc`
- Test (<https://uvadlo-dev.instructure.com>): `https://workflow-api-tst.datanose.nl/signin-oidc`

## Local testing
Local development and testing is possible using the `dev.json` config file. 
Note that the frontend and backend ports needs to be set correctly, and that the redirect URI needs to match the backend.

## Test refresh
In the current setup, the production test environment (uvadlo.test) is linked to our acceptance environment.
This environment is refreshed every four weeks, overwriting the LTI configuration.
For this purpose a redirect from production to acceptance has been configured if the LTI launch comes from the test environment.
To make this work, the acceptance redirect url needs to be added to the LTI key in <https://canvas.uva.nl>. 