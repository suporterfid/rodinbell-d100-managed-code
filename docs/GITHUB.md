# Working with the GitHub repository

Repository: [suporterfid/rodinbell-d100-managed-code](https://github.com/suporterfid/rodinbell-d100-managed-code).
The default branch is `main`.

## Clone and verify

```powershell
git clone https://github.com/suporterfid/rodinbell-d100-managed-code.git
cd rodinbell-d100-managed-code
pwsh ./scripts/verify.ps1
```

You can also clone this repository in GitHub Desktop or add an existing local clone.
See [contributing](../CONTRIBUTING.md) for development and validation guidance.

## Continuous integration

Pushes to `main` and pull requests trigger the Windows CI workflow. It restores
locked dependencies, builds, runs offline tests, and stores test results and the
`.nupkg` as workflow artifacts. It does not publish to NuGet or operate physical
hardware. Workflow runs are available on the
[Actions page](https://github.com/suporterfid/rodinbell-d100-managed-code/actions).

## Package releases

The package ID is `Rodinbell.D100`, initially version `0.1.0`. A local build does not
reserve a NuGet package name. Choose the next version and verify package ownership
before publishing to a package registry. GitHub repository publication and NuGet
package publication are separate operations.
