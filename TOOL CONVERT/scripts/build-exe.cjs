const { spawnSync } = require('child_process');
const path = require('path');

const builderBin = path.join(__dirname, '..', 'node_modules', 'electron-builder', 'cli.js');
const result = spawnSync(process.execPath, [builderBin, '--win', 'portable'], {
  stdio: 'inherit',
  env: {
    ...process.env,
    CSC_IDENTITY_AUTO_DISCOVERY: 'false',
    NODE_OPTIONS: [process.env.NODE_OPTIONS, '--no-deprecation'].filter(Boolean).join(' '),
  },
});

process.exit(result.status ?? 1);
