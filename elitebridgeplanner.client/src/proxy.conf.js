const { env } = require('process');

const target = env.ASPNETCORE_HTTPS_PORT
  ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
  : env.ASPNETCORE_URLS
    ? env.ASPNETCORE_URLS.split(';')[0]
    : 'https://localhost:7293';

const PROXY_CONFIG = [
  {
    context: ["/api"],
    target,
    secure: false
  },
  {
    context: ["/spansh-api"],
    target: "https://www.spansh.co.uk",
    secure: true,
    changeOrigin: true,
    pathRewrite: {
      "^/spansh-api": ""
    },
    logLevel: "debug"
  }
];

module.exports = PROXY_CONFIG;
