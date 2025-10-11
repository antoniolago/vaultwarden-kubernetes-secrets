#!/bin/bash

# Run the helm test job locally using act
# Uses the test-local.yml workflow optimized for local testing

echo "🚀 Running Helm test job with act..."
echo "📋 Workflow: test-local.yml"
echo "🎯 Event: pull_request"
echo ""

# Parse command line arguments
JOB=""
WORKFLOW="test-local.yml"

while [[ $# -gt 0 ]]; do
  case $1 in
    -j|--job)
      JOB="$2"
      shift 2
      ;;
    -w|--workflow)
      WORKFLOW="$2"
      shift 2
      ;;
    -h|--help)
      echo "Usage: $0 [OPTIONS]"
      echo ""
      echo "Options:"
      echo "  -j, --job JOB_NAME       Run specific job (default: all jobs)"
      echo "  -w, --workflow FILE      Use specific workflow file (default: test-local.yml)"
      echo "  -h, --help               Show this help message"
      echo ""
      echo "Available jobs in test-local.yml:"
      echo "  - build-and-test"
      echo "  - code-quality"
      echo "  - infrastructure-validation"
      echo "  - test-helm-chart"
      echo ""
      echo "Examples:"
      echo "  $0                                    # Run all jobs"
      echo "  $0 -j test-helm-chart                 # Run only helm test"
      echo "  $0 -j infrastructure-validation       # Run only infrastructure validation"
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      echo "Use -h or --help for usage information"
      exit 1
      ;;
  esac
done

# Build act command
ACT_CMD="act pull_request"
ACT_CMD="$ACT_CMD -W .github/workflows/$WORKFLOW"
ACT_CMD="$ACT_CMD --container-architecture linux/amd64"
ACT_CMD="$ACT_CMD -P ubuntu-latest=catthehacker/ubuntu:act-latest"

if [ -n "$JOB" ]; then
  ACT_CMD="$ACT_CMD -j $JOB"
  echo "🎯 Running job: $JOB"
else
  echo "🎯 Running all jobs"
fi

echo ""

# Run act
eval $ACT_CMD

echo ""
echo "✅ Test run completed"
