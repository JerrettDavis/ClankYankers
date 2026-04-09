FROM alpine:3.20

RUN apk add --no-cache bash openssh-server shadow

COPY sshd_config /etc/ssh/sshd_config
COPY entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh \
    && mkdir -p /var/run/sshd \
    && ssh-keygen -A

EXPOSE 22

CMD ["/entrypoint.sh"]
