# Contributing to Vaultwarden Kubernetes Secrets Sync

Thank you for considering contributing! This project aims to make secret management between Vaultwarden and Kubernetes seamless and secure. Every contribution helps make this tool better for the community.

---

## 🚀 Quick Start for Contributors

### Prerequisites

- **.NET 10.0 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/9.0))
- **Docker** (for testing container builds)
- **Kubernetes cluster** (local or remote - minikube, kind, k3s, etc.)
- **Vaultwarden instance** (or Bitwarden account for testing)
- **Git** and **GitHub account**

### Get the Code

```bash
# Fork the repository on GitHub, then:
git clone https://github.com/YOUR_USERNAME/vaultwarden-kubernetes-secrets.git
cd vaultwarden-kubernetes-secrets

# Add upstream remote
git remote add upstream https://github.com/antoniolago/vaultwarden-kubernetes-secrets.git
```

### Build and Run

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run tests
dotnet test

# Run the application
cd VaultwardenK8sSync
cp env.example .env
# Edit .env with your credentials
dotnet run sync
```

---

## 🎯 Ways to Contribute

We welcome all types of contributions:

### 🐛 Bug Reports

Found a bug? Help us squash it!

- Check [existing issues](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/issues) first
- Use the bug report template
- Include logs, environment details, and steps to reproduce

### ✨ Feature Requests

Have an idea? We'd love to hear it!

- Check if it's already requested
- Describe the use case and expected behavior
- Explain why it would benefit other users

### 📝 Documentation

Documentation is as important as code!

- Fix typos or unclear instructions
- Add examples and use cases
- Improve README or inline code comments
- Create tutorials or blog posts

### 🔧 Code Contributions

Ready to code? Here's what we need:

**High Priority:**

- Performance improvements (especially CPU usage during sync)
- Better error handling and recovery
- Additional item type support
- Enhanced logging and observability
- Security improvements

**Good First Issues:**

- Look for issues labeled [`good first issue`](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/labels/good%20first%20issue)
- These are well-scoped and beginner-friendly

---

## 🛠️ Development Workflow

### 1. Create a Feature Branch

```bash
# Update your fork
git checkout main
git pull upstream main

# Create a feature branch
git checkout -b feature/your-feature-name
# or
git checkout -b fix/issue-description
```

### 2. Make Your Changes

**Code Style:**

- Follow existing C# conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and concise

**Testing:**

- Add unit tests for new functionality
- Update existing tests if behavior changes
- Ensure all tests pass: `dotnet test`

**Commits:**

- Write clear, descriptive commit messages
- Use conventional commits format:
  ```
  feat: add support for Card item types
  fix: resolve namespace creation race condition
  docs: update configuration examples
  test: add integration tests for SSH keys
  refactor: simplify secret sanitization logic
  ```

### 3. Test Locally

```bash
# Run all tests
dotnet test

# Run specific test
dotnet test --filter "DisplaySpectreConsoleSummaryDemo"

# Test with dry run
SYNC__DRYRUN=true dotnet run sync

# Build Docker image
docker build -t vaultwarden-kubernetes-secrets:test ./VaultwardenK8sSync

# Test in Kubernetes (if available)
kubectl set image deployment/vaultwarden-kubernetes-secrets \
  vaultwarden-kubernetes-secrets=vaultwarden-kubernetes-secrets:test
```

### 4. Submit a Pull Request

```bash
# Push your branch
git push origin feature/your-feature-name
```

Then open a PR on GitHub:

- Use a clear, descriptive title
- Fill out the PR template completely
- Link related issues (e.g., "Fixes #123")
- Add screenshots/logs if relevant
- Mark as draft if work-in-progress

**What Happens Next:**

- Automated tests run (build, unit tests, security checks)
- A Docker image is built for your PR (tagged as `pr-XXX-XXXXXXXX`)
- Maintainers review your code
- You may be asked to make changes
- Once approved, your PR will be merged! 🎉

---

## 🧪 Testing Guide

### Unit Tests

```bash
# Run all tests
dotnet test

# Run with coverage (if configured)
dotnet test /p:CollectCoverage=true

# Run specific test class
dotnet test --filter "KubernetesServiceTests"
```

### Integration Testing

Create a test environment:

```bash
# 1. Start a local Kubernetes cluster
kind create cluster --name vw-test

# 2. Set up test Vaultwarden instance (or use existing)
# 3. Configure .env with test credentials
# 4. Run sync
SYNC__DRYRUN=true dotnet run sync
```

### Manual Testing Checklist

Before submitting a PR, test these scenarios:

- [ ] Sync creates new secrets correctly
- [ ] Sync updates existing secrets when items change
- [ ] Orphan cleanup removes old secrets
- [ ] Dry run mode doesn't modify anything
- [ ] Multiple namespaces work correctly
- [ ] Custom field names are respected
- [ ] Error handling works (invalid credentials, missing namespace, etc.)
- [ ] Logs are clear and helpful

---

## 📋 Code Review Guidelines

### What We Look For

**Functionality:**

- Does it solve the problem?
- Are edge cases handled?
- Is error handling robust?

**Code Quality:**

- Is it readable and maintainable?
- Are there tests?
- Is it well-documented?

**Performance:**

- Does it impact sync speed?
- Are there unnecessary API calls?
- Is memory usage reasonable?

**Security:**

- Are secrets handled safely?
- Is input validated?
- Are dependencies up-to-date?

### Review Process

1. **Automated Checks** - Must pass before human review
   - Build succeeds
   - Tests pass
   - No TODO/FIXME comments
   - Helm chart validates
   - No vulnerable dependencies

2. **Code Review** - Maintainer feedback
   - Functionality review
   - Code quality assessment
   - Suggestions for improvement

3. **Approval & Merge**
   - At least one maintainer approval required
   - All conversations resolved
   - Squash and merge (usually)

---

## 🏗️ Project Structure

```
vaultwarden-kubernetes-secrets/
├── VaultwardenK8sSync/          # Main application
│   ├── Application/             # Application host and command handling
│   ├── Configuration/           # Configuration and constants
│   ├── Infrastructure/          # Process execution utilities
│   ├── Models/                  # Data models and DTOs
│   ├── Services/                # Core business logic
│   │   ├── SyncService.cs       # Main sync orchestration
│   │   ├── KubernetesService.cs # K8s API interactions
│   │   └── SpectreConsoleSummaryFormatter.cs # Console output
│   ├── Dockerfile               # Container image definition
│   └── Program.cs               # Application entry point
├── VaultwardenK8sSync.Tests/    # Test project
├── charts/                      # Helm chart
│   └── vaultwarden-kubernetes-secrets/
├── .github/workflows/           # CI/CD pipelines
└── README.md                    # User documentation
```

### Key Components

**SyncService** - Core sync logic

- Fetches items from Vaultwarden
- Transforms to Kubernetes secrets
- Handles create/update/delete operations

**KubernetesService** - K8s interactions

- Secret CRUD operations
- Namespace validation
- Label-based filtering

**VaultwardenService** - Vaultwarden integration

- Authenticates via inner service to the VW API
- Fetches and filters items
- Handles organization/collection scoping

---

## 🎨 UI/UX Considerations

We use **Spectre.Console** for beautiful terminal output:

- Use colors meaningfully (green=success, yellow=warning, red=error)
- Keep output concise but informative
- Support both interactive and CI/CD environments
- Provide progress indicators for long operations
- Make errors actionable (tell users how to fix)

---

## 🔒 Security Best Practices

**When Contributing:**

1. **Never commit secrets** - Use `.env` files (gitignored)
2. **Validate all input** - Especially from Vaultwarden items
3. **Use secure defaults** - Fail closed, not open
4. **Log carefully** - Never log passwords or sensitive data
5. **Update dependencies** - Keep packages current
6. **Follow least privilege** - Request minimal K8s permissions

**Reporting Security Issues:**

If you discover a security vulnerability:

- **DO NOT** open a public issue
- Email the maintainers directly (see README)
- Provide details and steps to reproduce
- Allow time for a fix before public disclosure

---

## 📚 Resources

### Learning Resources

- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [Kubernetes Client for .NET](https://github.com/kubernetes-client/csharp)
- [Bitwarden CLI](https://bitwarden.com/help/cli/)
- [Spectre.Console](https://spectreconsole.net/)

### Project Links

- [Issue Tracker](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/issues)
- [Discussions](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/discussions)
- [Helm Chart](https://artifacthub.io/packages/search?repo=vaultwarden-kubernetes-secrets)

---

## 💬 Communication

### Where to Ask Questions

- **GitHub Discussions** - General questions, ideas, show-and-tell
- **GitHub Issues** - Bug reports, feature requests
- **Pull Requests** - Code-specific discussions

### Response Times

This is a community project maintained by volunteers:

- We aim to respond to issues within a few days
- PRs may take longer depending on complexity
- Be patient and respectful

---

## 🏆 Recognition

Contributors are recognized in:

- GitHub contributors page
- Release notes for significant contributions
- Project README (for major features)

Every contribution matters, from fixing typos to implementing major features!

---

## 📜 License

By contributing, you agree that your contributions will be licensed under the same license as the project (MIT License).

---

## ❓ Questions?

Not sure where to start? Have questions?

- Check the [README](README.md) and [detailed docs](VaultwardenK8sSync/README.md)
- Look at [existing PRs](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/pulls) for examples
- Open a [Discussion](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/discussions)
- Comment on an issue you're interested in

**We're here to help!** Don't hesitate to ask questions. Everyone was a beginner once.

---

## 🙏 Thank You!

Your contributions make this project better for everyone. Whether you're fixing a typo, reporting a bug, or implementing a major feature - thank you for being part of this community!

Happy coding! 🚀
