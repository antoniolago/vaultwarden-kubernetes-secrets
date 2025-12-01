#!/bin/bash
set -e

# Helm Chart Consistency and Best Practices Test
# Validates that the Helm chart follows best practices and maintains consistency

CHART_PATH="./charts/vaultwarden-kubernetes-secrets"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

TESTS_PASSED=0
TESTS_FAILED=0
WARNINGS=0

echo_pass() {
    echo -e "${GREEN}✓${NC} $1"
    ((TESTS_PASSED++))
}

echo_fail() {
    echo -e "${RED}✗${NC} $1"
    ((TESTS_FAILED++))
}

echo_warn() {
    echo -e "${YELLOW}⚠${NC} $1"
    ((WARNINGS++))
}

echo_info() {
    echo -e "ℹ $1"
}

echo_info "Helm Chart Consistency Tests"
echo_info "=============================="
echo_info ""

# Test 1: Chart.yaml validation
echo_info "Test 1: Chart.yaml Validation"
echo_info "------------------------------"

if [ -f "$CHART_PATH/Chart.yaml" ]; then
    echo_pass "Chart.yaml exists"
    
    # Check required fields
    if grep -q "^name:" "$CHART_PATH/Chart.yaml"; then
        echo_pass "Chart name is defined"
    else
        echo_fail "Chart name is missing"
    fi
    
    if grep -q "^version:" "$CHART_PATH/Chart.yaml"; then
        echo_pass "Chart version is defined"
    else
        echo_fail "Chart version is missing"
    fi
    
    if grep -q "^appVersion:" "$CHART_PATH/Chart.yaml"; then
        echo_pass "App version is defined"
    else
        echo_warn "App version is missing (recommended)"
    fi
    
    if grep -q "^description:" "$CHART_PATH/Chart.yaml"; then
        echo_pass "Chart description is defined"
    else
        echo_warn "Chart description is missing (recommended)"
    fi
else
    echo_fail "Chart.yaml not found"
fi

echo_info ""

# Test 2: values.yaml validation
echo_info "Test 2: values.yaml Validation"
echo_info "-------------------------------"

if [ -f "$CHART_PATH/values.yaml" ]; then
    echo_pass "values.yaml exists"
    
    # Check for security-sensitive defaults
    if grep -q 'DRYRUN.*:.*"false"' "$CHART_PATH/values.yaml"; then
        echo_warn "Default dry-run is false (consider true for safety)"
    else
        echo_pass "Dry-run default is safe"
    fi
    
    # Check for proper image tags
    if grep -q 'tag:.*"latest"' "$CHART_PATH/values.yaml"; then
        echo_warn "Image tag 'latest' found (not recommended for production)"
    else
        echo_pass "No 'latest' image tags in defaults"
    fi
    
    # Check for resource limits
    if grep -q "limits:" "$CHART_PATH/values.yaml" && grep -q "requests:" "$CHART_PATH/values.yaml"; then
        echo_pass "Resource limits and requests are defined"
    else
        echo_fail "Resource limits or requests are missing"
    fi
    
    # Check for security context
    if grep -q "securityContext:" "$CHART_PATH/values.yaml"; then
        echo_pass "Security context is defined"
    else
        echo_fail "Security context is missing"
    fi
else
    echo_fail "values.yaml not found"
fi

echo_info ""

# Test 3: Template validation
echo_info "Test 3: Template Validation"
echo_info "---------------------------"

REQUIRED_TEMPLATES=(
    "deployment.yaml"
    "serviceaccount.yaml"
    "rbac.yaml"
    "configmap.yaml"
)

for template in "${REQUIRED_TEMPLATES[@]}"; do
    if [ -f "$CHART_PATH/templates/$template" ]; then
        echo_pass "Template $template exists"
    else
        echo_fail "Template $template is missing"
    fi
done

echo_info ""

# Test 4: RBAC validation
echo_info "Test 4: RBAC Configuration"
echo_info "--------------------------"

if [ -f "$CHART_PATH/templates/rbac.yaml" ]; then
    # Check if RBAC can be disabled
    if grep -q "if.*\.rbac\.create" "$CHART_PATH/templates/rbac.yaml"; then
        echo_pass "RBAC can be conditionally disabled"
    else
        echo_warn "RBAC is always created (consider making it optional)"
    fi
    
    # Check for ClusterRole (needed for cluster-wide access)
    if grep -q "kind: ClusterRole" "$CHART_PATH/templates/rbac.yaml"; then
        echo_pass "ClusterRole is defined"
        
        # Verify necessary permissions
        if grep -q "secrets" "$CHART_PATH/templates/rbac.yaml"; then
            echo_pass "Secret permissions are granted"
        else
            echo_fail "Secret permissions are missing"
        fi
        
        if grep -q "namespaces" "$CHART_PATH/templates/rbac.yaml"; then
            echo_pass "Namespace permissions are granted"
        else
            echo_warn "Namespace permissions might be missing"
        fi
    else
        echo_warn "ClusterRole not found (might use Role instead)"
    fi
else
    echo_fail "RBAC template not found"
fi

echo_info ""

# Test 5: Security best practices
echo_info "Test 5: Security Best Practices"
echo_info "--------------------------------"

# Check deployment security
if [ -f "$CHART_PATH/templates/deployment.yaml" ]; then
    if grep -q "runAsNonRoot: true" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Pod runs as non-root user"
    else
        echo_fail "Pod does not enforce non-root user"
    fi
    
    if grep -q "readOnlyRootFilesystem: true" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Read-only root filesystem is enforced"
    else
        echo_warn "Read-only root filesystem not enforced"
    fi
    
    if grep -q "allowPrivilegeEscalation: false" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Privilege escalation is disabled"
    else
        echo_fail "Privilege escalation is not disabled"
    fi
    
    if grep -q "drop:" "$CHART_PATH/templates/deployment.yaml" && grep -q "ALL" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "All capabilities are dropped"
    else
        echo_warn "Capabilities dropping could be more restrictive"
    fi
fi

echo_info ""

# Test 6: Secrets management
echo_info "Test 6: Secrets Management"
echo_info "--------------------------"

if [ -f "$CHART_PATH/templates/deployment.yaml" ]; then
    # Check if secrets are referenced, not embedded
    if grep -q "secretKeyRef:" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Secrets are referenced via secretKeyRef"
    else
        echo_fail "Secrets might be embedded directly"
    fi
    
    # Check for sensitive values in env
    if grep -qE "(PASSWORD|SECRET|TOKEN|KEY).*value:" "$CHART_PATH/templates/deployment.yaml"; then
        echo_warn "Potential hardcoded sensitive values found"
    else
        echo_pass "No obvious hardcoded sensitive values"
    fi
fi

echo_info ""

# Test 7: Health checks
echo_info "Test 7: Health Checks"
echo_info "---------------------"

if [ -f "$CHART_PATH/templates/deployment.yaml" ]; then
    if grep -q "livenessProbe:" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Liveness probe is configured"
    else
        echo_warn "Liveness probe is missing"
    fi
    
    if grep -q "readinessProbe:" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Readiness probe is configured"
    else
        echo_warn "Readiness probe is missing"
    fi
    
    if grep -q "startupProbe:" "$CHART_PATH/templates/deployment.yaml"; then
        echo_pass "Startup probe is configured"
    else
        echo_warn "Startup probe is missing (recommended for slow-starting apps)"
    fi
fi

echo_info ""

# Test 8: Helm template rendering
echo_info "Test 8: Template Rendering Test"
echo_info "--------------------------------"

if helm template test-render $CHART_PATH \
    --set env.config.VAULTWARDEN__SERVERURL="https://test.example.com" \
    > /tmp/helm-consistency-test.yaml 2>&1; then
    echo_pass "Templates render without errors"
    
    # Validate YAML syntax
    if command -v yamllint &> /dev/null; then
        if yamllint /tmp/helm-consistency-test.yaml &> /dev/null; then
            echo_pass "Generated YAML is valid"
        else
            echo_warn "Generated YAML has linting issues"
        fi
    else
        echo_info "yamllint not installed, skipping YAML validation"
    fi
else
    echo_fail "Template rendering failed"
fi

echo_info ""

# Test 9: Labels and annotations
echo_info "Test 9: Labels and Annotations"
echo_info "------------------------------"

if grep -r "app.kubernetes.io/name" "$CHART_PATH/templates/" > /dev/null; then
    echo_pass "Standard Kubernetes labels are used"
else
    echo_warn "Standard Kubernetes labels might be missing"
fi

if grep -r "helm.sh/chart" "$CHART_PATH/templates/" > /dev/null; then
    echo_pass "Helm chart labels are present"
else
    echo_warn "Helm chart labels are missing"
fi

echo_info ""

# Test 10: Documentation
echo_info "Test 10: Documentation"
echo_info "----------------------"

if [ -f "$CHART_PATH/README.md" ]; then
    echo_pass "Chart README.md exists"
else
    echo_warn "Chart README.md is missing (recommended)"
fi

if [ -f "$CHART_PATH/templates/NOTES.txt" ]; then
    echo_pass "NOTES.txt exists for post-install instructions"
else
    echo_warn "NOTES.txt is missing (recommended)"
fi

echo_info ""

# Test 11: Helm test hooks
echo_info "Test 11: Helm Test Hooks"
echo_info "------------------------"

if [ -d "$CHART_PATH/tests" ] && [ "$(ls -A $CHART_PATH/tests)" ]; then
    echo_pass "Helm test hooks directory exists with content"
    
    TEST_COUNT=$(find "$CHART_PATH/tests" -name "*.yaml" | wc -l)
    echo_info "Found $TEST_COUNT test file(s)"
else
    echo_warn "No Helm test hooks found (recommended for validation)"
fi

echo_info ""

# Test 12: Values schema validation
echo_info "Test 12: Values Schema"
echo_info "----------------------"

if [ -f "$CHART_PATH/values.schema.json" ]; then
    echo_pass "values.schema.json exists for validation"
else
    echo_warn "values.schema.json is missing (recommended for Helm 3.1+)"
fi

echo_info ""

# Test 13: Chart dependencies
echo_info "Test 13: Chart Dependencies"
echo_info "---------------------------"

if grep -q "^dependencies:" "$CHART_PATH/Chart.yaml"; then
    echo_info "Chart has dependencies defined"
    
    if [ -d "$CHART_PATH/charts" ] && [ "$(ls -A $CHART_PATH/charts)" ]; then
        echo_pass "Dependencies are vendored"
    else
        echo_warn "Dependencies not vendored (run 'helm dependency update')"
    fi
else
    echo_pass "No external dependencies (self-contained chart)"
fi

echo_info ""

# Final Summary
echo_info "================================"
echo_info "Consistency Test Summary"
echo_info "================================"
echo_info ""
echo_info "Results:"
echo -e "  ${GREEN}Passed:${NC}   $TESTS_PASSED"
echo -e "  ${RED}Failed:${NC}   $TESTS_FAILED"
echo -e "  ${YELLOW}Warnings:${NC} $WARNINGS"
echo_info ""

if [ $TESTS_FAILED -eq 0 ]; then
    echo -e "${GREEN}All critical tests passed!${NC}"
    if [ $WARNINGS -gt 0 ]; then
        echo -e "${YELLOW}There are $WARNINGS warning(s) to review.${NC}"
    fi
    exit 0
else
    echo -e "${RED}$TESTS_FAILED test(s) failed!${NC}"
    exit 1
fi
