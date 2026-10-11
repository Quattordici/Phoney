# Security Policy

## Supported versions

Security fixes are applied to the latest released version of Phoney. Please update to the latest
release before reporting an issue.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Use GitHub's private vulnerability reporting instead: go to the **Security** tab of this repository and
choose **Report a vulnerability**. This creates a private advisory that only the maintainer can see.

Please include:

- The affected version(s) and target framework
- A description of the issue and its impact
- Steps or a minimal code sample to reproduce it

You can expect an initial response within a few days. Once a fix is available, it will be released to
NuGet and the advisory published, with credit to the reporter if desired.

## Scope

Phoney generates fake data for testing and development. It is not intended to produce cryptographically
secure random values or to be used for security purposes, so predictable output is not a vulnerability.
Issues such as unsafe handling of untrusted input, supply-chain concerns in the build or release
pipeline, or problems in the published packages are in scope.
