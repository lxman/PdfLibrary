# Releasing PdfLibrary

This document describes how to release new versions of PdfLibrary to NuGet.

## Prerequisites

1. **NuGet API Key**: You need a NuGet.org API key with push permissions
2. **GitHub Secret**: The API key must be stored as a GitHub repository secret named `NUGET_API_KEY`

### Setting up the NuGet API Key

1. Go to [NuGet.org](https://www.nuget.org/) and sign in
2. Navigate to your account → API Keys
3. Create a new API key with "Push" scope for "PdfLibrary*" packages
4. Copy the key (you won't see it again)

### Adding the Secret to GitHub

1. Go to your GitHub repository → Settings → Secrets and variables → Actions
2. Click "New repository secret"
3. Name: `NUGET_API_KEY`
4. Value: Your NuGet API key
5. Click "Add secret"

## Release Process

### 1. Update Version Numbers (Optional)

The version numbers in the csproj files are automatically updated by the GitHub Action based on the git tag. However, you may want to keep them in sync for local development:

- `PdfLibrary/PdfLibrary.csproj` - Update `<Version>` element
- `PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj` - Update `<Version>` element

**Important**: Both packages must have the same version number.

### 2. Create a GitHub Release

1. Go to your GitHub repository → Releases → "Create a new release"
2. Click "Choose a tag" and create a new tag (e.g., `v0.0.1` or `0.0.1`)
3. Set the release title (e.g., "v0.0.1 - Initial Release")
4. Write release notes describing what's included
5. Click "Publish release"

### 3. Automatic Publishing

When you publish the release, the GitHub Action will automatically:

1. Checkout the code
2. Extract the version from the git tag
3. Update the version in both csproj files
4. Build the solution in Release mode
5. Run tests
6. Create NuGet packages
7. Push packages to NuGet.org

### 4. Verify the Release

After a few minutes:

1. Check the GitHub Actions tab for the workflow status
2. Verify packages appear on NuGet.org:
   - https://www.nuget.org/packages/PdfLibrary
   - https://www.nuget.org/packages/PdfLibrary.Rendering.SkiaSharp

Note: NuGet packages may take 15-30 minutes to be indexed and searchable.

## Version Numbering

We use [Semantic Versioning](https://semver.org/):

- **MAJOR.MINOR.PATCH** (e.g., 1.2.3)
- **MAJOR**: Breaking API changes
- **MINOR**: New features, backward compatible
- **PATCH**: Bug fixes, backward compatible

For pre-releases:
- `0.x.x` - Initial development, API may change
- `x.x.x-alpha` - Alpha release
- `x.x.x-beta` - Beta release
- `x.x.x-rc.1` - Release candidate

## Package Dependencies

`PdfLibrary.Rendering.SkiaSharp` depends on `PdfLibrary` with an exact version constraint. Both packages must always be released together with the same version number.

## Troubleshooting

### Build fails
- Check the GitHub Actions log for detailed errors
- Ensure all tests pass locally before releasing

### Push to NuGet fails
- Verify the `NUGET_API_KEY` secret is set correctly
- Check if the API key has expired
- Ensure the package version doesn't already exist on NuGet

### Package not appearing on NuGet
- Wait 15-30 minutes for indexing
- Check the NuGet.org website directly (search may lag behind)
