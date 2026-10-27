# Root setup for the plain Ubuntu images: adds the libraries an Ubuntu desktop already has and the updater
# uses (fontconfig, X11, OpenSSL), plus any packages named as arguments. FUSE is added only when "fuse3" is named.
set -e
if [ -n "${HTTPS_PROXY:-}" ]; then
	sed -i 's|http://|https://|g' /etc/apt/sources.list.d/*.sources /etc/apt/sources.list 2>/dev/null || true
	{
		echo "Acquire::https::Proxy \"$HTTPS_PROXY\";"
		[ -n "${SSL_CERT_FILE:-}" ] && echo "Acquire::https::CaInfo \"$SSL_CERT_FILE\";"
	} > /etc/apt/apt.conf.d/99proxy
fi
apt-get update -qq
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends libfontconfig1 fonts-dejavu-core \
	libx11-6 libice6 libsm6 libxrandr2 libxi6 libxcursor1 libxext6 libxrender1 "$@" >/dev/null
