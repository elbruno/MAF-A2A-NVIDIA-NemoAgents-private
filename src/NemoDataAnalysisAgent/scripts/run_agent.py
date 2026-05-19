#!/usr/bin/env python3
"""
NeMo Data Analysis Agent Startup Script
Initializes and runs the NeMo agent with A2A support
"""

import sys
import os
import argparse
import logging
from pathlib import Path
from dotenv import load_dotenv
import asyncio

# Add the nemo tools to the path
SCRIPT_DIR = Path(__file__).parent
REPO_ROOT = SCRIPT_DIR.parent.parent.parent.parent
NEMO_DIR = SCRIPT_DIR.parent

sys.path.insert(0, str(NEMO_DIR))
sys.path.insert(0, str(REPO_ROOT))

# Load environment variables
env_path = REPO_ROOT / ".env"
if env_path.exists():
    load_dotenv(env_path)

def configure_logging(log_level: str = None):
    """Configure logging for the agent"""
    level = log_level or os.getenv("NEMO_LOG_LEVEL", "INFO")
    logging.basicConfig(
        level=level,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )
    return logging.getLogger("nemo-data-analysis-agent")

def validate_environment():
    """Validate required environment variables"""
    provider = os.getenv("NEMO_PROVIDER", "nim")
    
    if provider == "nim":
        if not os.getenv("NVIDIA_API_KEY"):
            raise ValueError("NVIDIA_API_KEY environment variable is required for NIM provider")
    elif provider == "azure-openai":
        required = ["AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_API_KEY", "AZURE_OPENAI_DEPLOYMENT_NAME"]
        missing = [var for var in required if not os.getenv(var)]
        if missing:
            raise ValueError(f"Missing environment variables for Azure OpenAI: {', '.join(missing)}")
    else:
        raise ValueError(f"Unsupported provider: {provider}")
    
    print(f"✓ Environment validation passed (Provider: {provider})")

async def run_nemo_agent(provider: str, host: str, port: int):
    """
    Run the NeMo agent with A2A support
    
    This would use the NeMo Agent Toolkit CLI:
    nat a2a serve --config_file workflow.yml --host {host} --port {port}
    """
    logger = configure_logging()
    
    logger.info(f"Starting NeMo Data Analysis Agent...")
    logger.info(f"Provider: {provider}")
    logger.info(f"Endpoint: http://{host}:{port}")
    logger.info(f"A2A endpoint: /.well-known/agent-card.json")
    
    # The actual agent startup would use subprocess to call 'nat a2a serve'
    # For now, we provide the command that should be run
    
    if provider == "nim":
        config_file = "workflow.yml"
    else:
        config_file = "workflow.azure-openai.yml"
    
    cmd = [
        "nat",
        "a2a",
        "serve",
        f"--config_file={NEMO_DIR / config_file}",
        f"--host={host}",
        f"--port={port}",
        '--name="nemo-data-analysis-agent"'
    ]
    
    logger.info(f"Run this command: {' '.join(cmd)}")
    logger.info("Note: Ensure NeMo Toolkit CLI (nat) is installed: pip install nvidia-nat-a2a")
    
    # Placeholder for actual agent startup
    logger.info("Agent would start here...")
    await asyncio.sleep(1)

def main():
    parser = argparse.ArgumentParser(
        description="Start NeMo Data Analysis Agent with A2A support"
    )
    parser.add_argument(
        "--provider",
        choices=["nim", "azure-openai"],
        default="nim",
        help="LLM provider to use (default: nim)"
    )
    parser.add_argument(
        "--host",
        default=os.getenv("NEMO_HOST", "127.0.0.1"),
        help="Host to bind to (default: 127.0.0.1)"
    )
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.getenv("NEMO_PORT", 8088)),
        help="Port to listen on (default: 8088)"
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        help="Logging level (default: INFO)"
    )
    
    args = parser.parse_args()
    
    try:
        validate_environment()
        asyncio.run(run_nemo_agent(args.provider, args.host, args.port))
    except KeyboardInterrupt:
        print("\n✓ Agent stopped by user")
        sys.exit(0)
    except Exception as e:
        print(f"✗ Error: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
