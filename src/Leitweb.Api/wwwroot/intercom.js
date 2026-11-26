class Intercom {
  constructor(root, getToken, notify) {
    this.root = root;
    this.getToken = getToken;
    this.notify = notify;
    this.clients = root.querySelector('#intercom-clients');
    this.channels = root.querySelector('#intercom-channels');
    this.audioButton = root.querySelector('#intercom-audio');
    this.talkButton = root.querySelector('#intercom-talk');
    this.endButton = root.querySelector('#intercom-end');
    this.acceptButton = root.querySelector('#intercom-accept');
    this.status = root.querySelector('#intercom-status');
    this.peers = new Map();
    this.snapshot = {clients: [], peers: []};
    this.message = 'Verbindung wird hergestellt …';
    this.stopped = true;

    this.audioButton.onclick = () => this.toggleAudio();
    this.clients.onchange = () => {
      if (this.clients.value) this.send({type: 'invite', target: this.clients.value});
      this.clients.value = '';
    };
    this.channels.onchange = () => {
      this.release();
      this.send(this.channels.value ? {type: 'join', target: this.channels.value} : {type: 'leave'});
      this.channels.value = this.selectedChannel();
    };
    this.acceptButton.onclick = () => {
      this.release();
      this.send({type: 'accept', target: this.snapshot.incoming});
    };
    this.endButton.onclick = () => {
      this.release();
      this.rejoinChannel = '';
      this.send({type: this.snapshot.incoming || this.snapshot.outgoing ? 'decline' : 'leave'});
    };

    this.talkButton.onpointerdown = event => {
      if (event.button !== 0 || !event.isPrimary) return;
      this.talkButton.setPointerCapture(event.pointerId);
      this.press();
    };
    for (const event of ['pointerup', 'pointercancel', 'lostpointercapture', 'blur']) {
      this.talkButton.addEventListener(event, () => this.release());
    }
    this.talkButton.onkeydown = event => {
      if (event.code !== 'Space' && event.code !== 'Enter') return;
      event.preventDefault();
      if (!event.repeat) this.press();
    };
    this.talkButton.onkeyup = event => {
      if (event.code !== 'Space' && event.code !== 'Enter') return;
      event.preventDefault();
      this.release();
    };
    window.addEventListener('blur', () => this.release());
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) this.release();
    });
    window.addEventListener('pagehide', () => {
      this.resumeOnReturn = !this.stopped;
      this.stop();
    });
    window.addEventListener('pageshow', event => {
      if (event.persisted && this.resumeOnReturn) this.start();
    });
  }

  start() {
    this.root.classList.remove('hidden');
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || !window.RTCPeerConnection) {
      this.message = 'Intercom benötigt HTTPS und einen Browser mit WebRTC.';
      this.render();
      return;
    }
    this.stopped = false;
    void this.connect();
  }

  async connect() {
    if (this.connecting || this.socket || this.stopped) return;
    this.connecting = true;
    try {
      const token = await this.getToken();
      if (this.stopped) return;
      const url = new URL('/ws/intercom', location.href);
      url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
      const protocols = ['leitweb-intercom'];
      if (token) protocols.push('bearer.' + token);
      const socket = new WebSocket(url, protocols);
      this.socket = socket;
      this.socketToken = token;
      this.lastPong = Date.now();
      this.heartbeat = setInterval(() => {
        if (Date.now() - this.lastPong > 45_000) socket.close();
        else {
          this.send({type: 'ping'});
          if (this.socketToken && this.renewalKey) void this.renew(socket);
        }
      }, 15_000);
      socket.onmessage = event => {
        if (this.socket === socket) this.receive(JSON.parse(event.data));
      };
      socket.onclose = () => {
        if (this.socket !== socket) return;
        this.socket = null;
        clearInterval(this.heartbeat);
        this.resetConnection();
        this.scheduleReconnect();
      };
    } catch {
      this.scheduleReconnect();
    } finally {
      this.connecting = false;
    }
  }

  async renew(socket) {
    try {
      const token = await this.getToken();
      if (this.socket !== socket || !token || token === this.socketToken) return;
      const response = await fetch('/api/v1/intercom/session', {
        method: 'POST',
        headers: {Authorization: `Bearer ${token}`, 'Content-Type': 'application/json'},
        body: JSON.stringify({key: this.renewalKey})
      });
      if (this.socket !== socket) return;
      if (response.ok) this.socketToken = token;
      else if (response.status === 404) socket.close();
    } catch {
      // The heartbeat retries transient renewal failures while the session remains valid.
    }
  }

  scheduleReconnect() {
    if (this.stopped) return;
    this.message = 'Intercom-Verbindung unterbrochen. Neuer Versuch …';
    this.render();
    clearTimeout(this.reconnect);
    this.reconnect = setTimeout(() => this.connect(), 2500);
  }

  resetConnection() {
    this.release();
    this.rejoinChannel = this.selectedChannel() || this.rejoinChannel;
    this.id = null;
    this.renewalKey = null;
    this.snapshot = {clients: [], peers: []};
    this.closePeers();
  }

  stop() {
    this.stopped = true;
    clearTimeout(this.reconnect);
    clearInterval(this.heartbeat);
    this.disableAudio();
    const socket = this.socket;
    this.socket = null;
    this.resetConnection();
    socket?.close();
  }

  send(message) {
    if (this.socket?.readyState !== WebSocket.OPEN) return false;
    this.socket.send(JSON.stringify(message));
    return true;
  }

  receive(message) {
    switch (message.type) {
      case 'hello':
        this.id = message.id;
        this.renewalKey = message.renewalKey;
        this.iceServers = message.iceServers;
        this.lastPong = Date.now();
        this.message = '';
        if (this.stream) {
          this.send({type: 'ready'});
          if (this.rejoinChannel) this.send({type: 'join', target: this.rejoinChannel});
        }
        this.rejoinChannel = '';
        break;
      case 'pong':
        this.lastPong = Date.now();
        return;
      case 'state':
        if (this.snapshot.room !== message.room) {
          this.release();
          this.closePeers();
        }
        this.snapshot = message;
        if (this.hold && message.request === this.hold.id && message.speaker === this.id)
          this.hold.granted = true;
        else if (this.hold?.granted)
          this.release();
        this.syncPeers();
        if (!this.connectedCount()) this.release();
        this.applyAudio();
        break;
      case 'signal': {
        const peer = this.peers.get(message.from);
        if (peer && peer.room === message.room)
          this.enqueuePeer(peer, () => this.applySignal(peer, message.data));
        return;
      }
      case 'error':
        if (message.request && message.request === this.hold?.id) this.release();
        this.notify(message.message, true);
        break;
    }
    this.render();
  }

  async toggleAudio() {
    if (this.stream) {
      this.disableAudio();
      this.render();
      return;
    }
    if (!this.id || this.enabling) return;
    const attempt = {};
    this.enabling = attempt;
    this.render();
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        audio: {echoCancellation: true, noiseSuppression: true, autoGainControl: true}, video: false
      });
      if (this.enabling !== attempt) return;
      const [track] = stream.getAudioTracks();
      track.enabled = false;
      track.onended = () => {
        this.disableAudio();
        this.notify('Das Mikrofon ist nicht mehr verfügbar.', true);
        this.render();
      };
      this.stream = stream;
      this.send({type: 'ready'});
    } catch {
      if (this.enabling === attempt) this.notify('Mikrofonzugriff nicht verfügbar.', true);
    } finally {
      if (this.stream !== stream) {
        stream?.getTracks().forEach(track => track.stop());
      }
      if (this.enabling === attempt) this.enabling = null;
      this.render();
    }
  }

  disableAudio() {
    this.enabling = null;
    this.rejoinChannel = '';
    this.release();
    this.send({type: 'unready'});
    this.closePeers();
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = null;
  }

  selectedChannel() {
    return this.snapshot.room?.startsWith('channel-') ? this.snapshot.room : '';
  }

  press() {
    if (!this.canTalk() || this.hold) return;
    const hold = {id: crypto.randomUUID(), room: this.snapshot.room, granted: false};
    if (this.send({type: 'press', room: hold.room, request: hold.id})) this.hold = hold;
    this.render();
  }

  release() {
    if (!this.hold) return;
    const {room, id} = this.hold;
    this.hold = null;
    this.applyAudio();
    this.send({type: 'release', room, request: id});
    this.render();
  }

  canTalk() {
    return !!this.stream && !!this.id && !!this.snapshot.room && this.connectedCount() > 0 &&
      (!this.snapshot.speaker || this.snapshot.speaker === this.id);
  }

  applyAudio() {
    const [track] = this.stream?.getAudioTracks() ?? [];
    if (track) track.enabled = this.hold?.granted === true;
    for (const peer of this.peers.values()) {
      if (!peer.audio) continue;
      peer.audio.muted = this.snapshot.speaker !== peer.id;
      if (!peer.audio.muted && peer.audio.paused) this.play(peer);
    }
  }

  connectedCount() {
    return [...this.peers.values()].filter(peer => peer.connection.connectionState === 'connected').length;
  }

  syncPeers() {
    const {room, peers} = this.snapshot;
    for (const [id, peer] of this.peers) {
      if (!peers.includes(id)) this.closePeer(peer);
    }
    if (!this.stream || !room) return;
    for (const id of peers) {
      if (!this.peers.has(id)) this.addPeer(id, room);
    }
  }

  addPeer(id, room) {
    const connection = new RTCPeerConnection({iceServers: this.iceServers});
    const peer = {id, room, connection, candidates: [], operations: Promise.resolve()};
    this.peers.set(id, peer);
    const [track] = this.stream.getAudioTracks();
    connection.addTrack(track, this.stream);
    connection.ontrack = event => {
      if (!this.isCurrent(peer) || event.track.kind !== 'audio' || peer.audio) return;
      const audio = new Audio();
      audio.muted = this.snapshot.speaker !== peer.id;
      audio.srcObject = new MediaStream([event.track]);
      peer.audio = audio;
      this.play(peer);
    };
    connection.onicecandidate = event => {
      if (event.candidate) this.signal(peer, {kind: 'candidate', candidate: event.candidate.toJSON()});
    };
    connection.onconnectionstatechange = () => {
      if (!this.isCurrent(peer)) return;
      if (!this.connectedCount()) this.release();
      if (connection.connectionState === 'failed' && this.id < id && !peer.restarted) {
        peer.restarted = true;
        this.enqueuePeer(peer, async () => {
          if (connection.signalingState === 'stable') await this.offer(peer, {iceRestart: true});
        });
      }
      this.render();
    };
    if (this.id < id) this.enqueuePeer(peer, () => this.offer(peer));
  }

  play(peer) {
    void peer.audio.play().catch(error => {
      if (!this.isCurrent(peer) || peer.audio.muted || error.name !== 'NotAllowedError') return;
      this.notify('Der Browser blockiert die Audiowiedergabe für diese Seite.', true);
    });
  }

  isCurrent(peer) {
    return this.peers.get(peer.id) === peer && this.snapshot.room === peer.room;
  }

  enqueuePeer(peer, operation) {
    peer.operations = peer.operations.then(async () => {
      if (this.isCurrent(peer)) await operation();
    }).catch(() => {
      if (!this.isCurrent(peer)) return;
      peer.failed = true;
      this.render();
    });
  }

  signal(peer, data) {
    if (this.isCurrent(peer)) this.send({type: 'signal', target: peer.id, room: peer.room, data});
  }

  async offer(peer, options) {
    await peer.connection.setLocalDescription(await peer.connection.createOffer(options));
    this.signal(peer, {kind: 'offer', sdp: peer.connection.localDescription.sdp});
  }

  async applySignal(peer, data) {
    const connection = peer.connection;
    if (data.kind === 'candidate') {
      if (connection.remoteDescription) await connection.addIceCandidate(data.candidate);
      else if (peer.candidates.length < 256) peer.candidates.push(data.candidate);
      return;
    }
    const expected = this.id < peer.id ? 'answer' : 'offer';
    if (data.kind !== expected || typeof data.sdp !== 'string') return;
    await connection.setRemoteDescription({type: data.kind, sdp: data.sdp});
    for (const candidate of peer.candidates.splice(0)) await connection.addIceCandidate(candidate);
    if (data.kind === 'offer') {
      await connection.setLocalDescription(await connection.createAnswer());
      this.signal(peer, {kind: 'answer', sdp: connection.localDescription.sdp});
    }
  }

  closePeer(peer) {
    this.peers.delete(peer.id);
    if (peer.audio) {
      peer.audio.pause();
      peer.audio.srcObject = null;
    }
    peer.connection.close();
  }

  closePeers() {
    for (const peer of this.peers.values()) this.closePeer(peer);
  }

  name(id) {
    const client = this.snapshot.clients.find(candidate => candidate.id === id);
    if (!client) return 'Client';
    return this.snapshot.clients.some(other => other.id !== id && other.name === client.name)
      ? `${client.name} (${id.slice(0, 6)})` : client.name;
  }

  render() {
    const {room, peers, clients, incoming, outgoing, speaker} = this.snapshot;
    const enabled = !!this.stream && !!this.id;
    const direct = room && !this.selectedChannel();
    const options = [new Option('Angemeldete Clients', '')];
    for (const client of clients) {
      if (client.id === this.id) continue;
      const option = new Option(this.name(client.id) + (client.available ? '' : ' · nicht verfügbar'), client.id);
      option.disabled = !client.available;
      options.push(option);
    }
    const optionsKey = JSON.stringify(options.map(option => [option.value, option.text, option.disabled]));
    if (this.clientOptionsKey !== optionsKey) {
      this.clientOptionsKey = optionsKey;
      this.clients.replaceChildren(...options);
    }
    this.clients.disabled = !enabled || !!direct || !!incoming || !!outgoing || options.length < 2;
    this.channels.disabled = !enabled;
    this.channels.value = this.selectedChannel();
    this.audioButton.disabled = !this.id || !!this.enabling;
    this.audioButton.textContent = this.stream ? 'Audio aus' : 'Audio aktivieren';
    this.talkButton.disabled = !this.canTalk();
    this.talkButton.setAttribute('aria-pressed', String(this.hold?.granted === true));
    this.acceptButton.classList.toggle('hidden', !enabled || !incoming);
    this.endButton.classList.toggle('hidden', !enabled || !(room || incoming || outgoing));
    this.endButton.textContent = incoming ? 'Ablehnen' : outgoing ? 'Anruf abbrechen' : 'Verlassen';

    let status = this.message;
    if (this.id) {
      if (incoming) status = `Anruf von ${this.name(incoming)}`;
      else if (outgoing) status = `Rufe ${this.name(outgoing)} …`;
      else if (room) {
        const label = direct ? this.name(peers[0]) : `Kanal ${room.at(-1)}`;
        const connected = this.connectedCount();
        const failed = [...this.peers.values()].some(peer =>
          peer.failed || peer.connection.connectionState === 'failed');
        const detail = speaker ? (speaker === this.id ? 'Du sprichst' : `${this.name(speaker)} spricht`)
          : !peers.length ? 'Warte auf Teilnehmer'
          : connected < peers.length ? (failed ? `Audioverbindung gestört (${connected}/${peers.length})` : `Verbunden: ${connected}/${peers.length}`)
          : 'Sprechbereit';
        status = `${label} · ${detail}`;
      } else status = this.stream ? 'Intercom bereit' : 'Audio für Intercom aktivieren';
    }
    if (this.status.textContent !== status) this.status.textContent = status;
  }
}
