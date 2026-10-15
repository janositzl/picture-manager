Your nginx runs in Docker (proxying to host.docker.internal:5080), so I've assumed a self-signed certificate for a LAN host. I've used pm.lan and 192.168.1.50 as placeholders, so replace them with your hostname and IP.

1. Generate the certificate (OpenSSL)
The simplest option is a self-signed certificate with SANs. Browsers require SANs, and the CN alone is ignored.


mkdir -p docker/certs && cd docker/certs

openssl req -x509 -nodes -newkey rsa:2048 -sha256 -days 825 \
  -keyout pm.key -out pm.crt \
  -subj "/CN=pm.lan" \
  -addext "subjectAltName=DNS:pm.lan,DNS:localhost,IP:192.168.1.50" \
  -addext "basicConstraints=CA:FALSE" \
  -addext "keyUsage=digitalSignature,keyEncipherment" \
  -addext "extendedKeyUsage=serverAuth"
Alternative: mkcert gives a locally trusted CA and avoids browser warnings on your own machines.


winget install FiloSottile.mkcert
mkcert -install
mkcert -cert-file pm.crt -key-file pm.key pm.lan localhost 192.168.1.50
If the site is public, use Let's Encrypt instead:


sudo certbot certonly --standalone -d your.domain.com
# produces /etc/letsencrypt/live/your.domain.com/{fullchain,privkey}.pem
2. Install the certificate on clients (self-signed only)
Skip this step with Let's Encrypt. With mkcert, mkcert -install already trusted it on that machine. For other machines, copy rootCA.pem from mkcert -CAROOT and import it as shown below.

Windows (admin PowerShell):


Import-Certificate -FilePath .\pm.crt -CertStoreLocation Cert:\LocalMachine\Root
Linux (Debian/Ubuntu):


sudo cp pm.crt /usr/local/share/ca-certificates/pm.crt
sudo update-ca-certificates
macOS:


sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain pm.crt
Firefox keeps its own store. Import the certificate under Settings → Certificates → Authorities.

3. Configure nginx
Replace docker/nginx.prod.conf with:


map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

# HTTP -> HTTPS redirect
server {
    listen 80;
    server_name _;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name pm.lan;

    ssl_certificate     /etc/nginx/certs/pm.crt;
    ssl_certificate_key /etc/nginx/certs/pm.key;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_session_cache   shared:SSL:10m;
    ssl_session_timeout 1d;

    add_header Strict-Transport-Security "max-age=31536000" always;

    location = /pm { return 301 /pm/; }

    location /pm/ {
        proxy_pass http://host.docker.internal:5080/;

        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Prefix /pm;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;

        proxy_read_timeout 300s;
        client_max_body_size 50m;
    }
}
Mount the certificate and publish port 443. In your nginx compose service or docker run:


services:
  nginx:
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./docker/nginx.prod.conf:/etc/nginx/conf.d/default.conf:ro
      - ./docker/certs:/etc/nginx/certs:ro
    extra_hosts:
      - "host.docker.internal:host-gateway"
4. Test and reload

docker compose exec nginx nginx -t
docker compose exec nginx nginx -s reload
# or: docker compose up -d --force-recreate nginx

curl -v https://pm.lan/pm/            # trusted cert
curl -vk https://192.168.1.50/pm/     # -k skips verification
openssl x509 -in docker/certs/pm.crt -noout -subject -dates -ext subjectAltName
5. Notes
DNS: pm.lan must resolve on the clients. Add 192.168.1.50 pm.lan to the hosts file (C:\Windows\System32\drivers\etc\hosts on Windows) or to your router's DNS.
Firewall: open TCP 443 on the host.
Key file: keep pm.key out of git. Add docker/certs/ to .gitignore.
Forwarded headers: the config already sends X-Forwarded-Proto $scheme, which is now https. If ASP.NET generates http:// URLs or redirects, make sure UseForwardedHeaders is enabled for XForwardedProto and XForwardedHost.
Renewal: the self-signed certificate above expires after 825 days. Re-run the openssl command before then and reload nginx.
I can apply the nginx and compose changes to the repo if you'd like.

-----------------------------------------------------------------------------------------------------------------

I found that HTTPS is already set up in the repo, so I changed very little.

Already in place (untracked, in docker/nginx/):

conf.d/default.conf redirects port 80 to HTTPS and serves 443 with TLS 1.2/1.3, HTTP/2 and HSTS. It proxies /pm/ to host.docker.internal:5080.
docker-compose.yaml publishes 80 and 443 and mounts ./conf.d and ./certs.
certs/create_cert.bat generates the self-signed dminipc.crt and dminipc.key, with SANs for dminipc, localhost and 192.168.0.50.
What I changed: I added *.key to .gitignore. dminipc.key was untracked but not ignored, so a git add . would have committed the private key. I confirmed it is now ignored and that no .key file is tracked.

What I left alone:

docker/nginx.prod.conf is the old HTTP-only config and docker/nginx/conf.d/default.conf now supersedes it. I left it in place because it's untracked and may be referenced elsewhere. You can delete it if nothing uses it.
docker/certs/ is an empty directory. You can remove it.
I didn't create a second certificate or config, since a duplicate would conflict with these files.
To finish the setup, you still need to do two things:

Trust the certificate on each client by importing dminipc.crt as in step 2 of my earlier list. For example, in an admin PowerShell: Import-Certificate -FilePath .\dminipc.crt -CertStoreLocation Cert:\LocalMachine\Root.
Start or reload nginx with docker compose -f docker/nginx/docker-compose.yaml up -d, then run docker exec nginx-proxy nginx -t.
The certificate expires 825 days after you generated it, so re-run create_cert.bat before then.