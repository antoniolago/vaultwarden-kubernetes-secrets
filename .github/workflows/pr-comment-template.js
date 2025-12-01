module.exports = function generateCommentBody(context, prTag, githubSha) {
  return `🚀 **PR Docker Image Ready for Testing**

✅ **Build Status**: Successfully built and pushed Docker image for this PR

📦 **Docker Image**: \`ghcr.io/${context.repo.owner}/vaultwarden-kubernetes-secrets:${prTag}\`

🔧 **How to Test**:
1. Update your deployment to use this image:
   \`\`\`yaml
   image: ghcr.io/${context.repo.owner}/vaultwarden-kubernetes-secrets:${prTag}
   \`\`\`

2. Or update your Helm values:
   \`\`\`yaml
   image:
     repository: ghcr.io/${context.repo.owner}/vaultwarden-kubernetes-secrets
     tag: ${prTag}
   \`\`\`

3. Or use kubectl to update existing deployment:
   \`\`\`bash
   kubectl set image deployment/vaultwarden-kubernetes-secrets vaultwarden-kubernetes-secrets=ghcr.io/${context.repo.owner}/vaultwarden-kubernetes-secrets:${prTag}
   \`\`\`

📝 **Note**: This image contains the changes from this PR and is ready for testing. The image will be automatically cleaned up after the PR is closed or merged.

---
*Last updated: ${new Date().toISOString()}*
*Build SHA: ${githubSha}*`;
};
