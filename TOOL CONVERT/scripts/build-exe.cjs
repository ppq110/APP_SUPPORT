const { spawnSync } = require('child_process');
const path = require('path');

// Hai bản build:
//   node scripts/build-exe.cjs             -> bản CÓ đăng nhập      -> release/
//   node scripts/build-exe.cjs --no-login  -> bản KHÔNG cần tài khoản -> release-nologin/
// (bản không đăng nhập tự chạy vite build với VITE_REQUIRE_LOGIN=false nên không cần `npm run build` trước)
const noLogin = process.argv.includes('--no-login');
const root = path.join(__dirname, '..');

function run(bin, args, extraEnv = {}) {
  const result = spawnSync(process.execPath, [bin, ...args], {
    stdio: 'inherit',
    cwd: root,
    env: {
      ...process.env,
      CSC_IDENTITY_AUTO_DISCOVERY: 'false',
      NODE_OPTIONS: [process.env.NODE_OPTIONS, '--no-deprecation'].filter(Boolean).join(' '),
      ...extraEnv,
    },
  });
  if ((result.status ?? 1) !== 0) process.exit(result.status ?? 1);
}

if (noLogin) {
  run(path.join(root, 'node_modules', 'vite', 'bin', 'vite.js'), ['build'], { VITE_REQUIRE_LOGIN: 'false' });
}

const builderArgs = ['--win', 'portable'];
if (noLogin) {
  builderArgs.push('-c.extraMetadata.requireLogin=false', '-c.directories.output=release-nologin');
}
run(path.join(root, 'node_modules', 'electron-builder', 'cli.js'), builderArgs);
