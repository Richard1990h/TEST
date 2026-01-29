/**
 * Proxy server for .NET Blazor Frontend
 * This Express server proxies requests to the .NET Blazor frontend running on a different port
 */
const express = require('express');
const httpProxy = require('http-proxy');
const { spawn } = require('child_process');

const app = express();
const proxy = httpProxy.createProxyServer({});

const BLAZOR_URL = 'http://127.0.0.1:5000';
let dotnetProcess = null;

function startBlazorFrontend() {
    const env = { ...process.env };
    env.DOTNET_ROOT = '/usr/share/dotnet';
    env.PATH = `/usr/share/dotnet:${env.PATH || ''}`;
    env.HOME = '/root';
    env.DOTNET_CLI_HOME = '/root';

    dotnetProcess = spawn('/usr/share/dotnet/dotnet', ['run', '--no-build', '-c', 'Release', '--urls', 'http://0.0.0.0:5000'], {
        cwd: '/app/Frontend',
        env: env,
        stdio: ['ignore', 'pipe', 'pipe']
    });

    dotnetProcess.stdout.on('data', (data) => {
        console.log(`[Blazor] ${data}`);
    });

    dotnetProcess.stderr.on('data', (data) => {
        console.error(`[Blazor Error] ${data}`);
    });

    dotnetProcess.on('close', (code) => {
        console.log(`Blazor process exited with code ${code}`);
    });

    console.log(`Started Blazor frontend with PID ${dotnetProcess.pid}`);
}

// Start Blazor on server start
startBlazorFrontend();

// Give Blazor time to start
setTimeout(() => {
    console.log('Blazor frontend should be ready');
}, 5000);

// Proxy all requests to Blazor
app.all('*', (req, res) => {
    proxy.web(req, res, { target: BLAZOR_URL }, (err) => {
        console.error('Proxy error:', err.message);
        res.status(502).send('Error connecting to Blazor frontend');
    });
});

// Handle WebSocket connections for Blazor SignalR
proxy.on('error', (err, req, res) => {
    console.error('Proxy error:', err.message);
});

// Cleanup on exit
process.on('SIGTERM', () => {
    if (dotnetProcess) {
        dotnetProcess.kill();
    }
    process.exit(0);
});

process.on('SIGINT', () => {
    if (dotnetProcess) {
        dotnetProcess.kill();
    }
    process.exit(0);
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, '0.0.0.0', () => {
    console.log(`Frontend proxy listening on port ${PORT}`);
});
