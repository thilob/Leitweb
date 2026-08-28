const organizationId = '11111111-1111-1111-1111-111111111111';
const incidentStatus = ['Offen', 'Disponiert', 'In Bearbeitung', 'Abgeschlossen', 'Storniert'];
const resourceStatus = ['Nicht verfügbar', 'Einsatzbereit', 'Disponiert', 'An Einsatzstelle'];
const occasions = ['Sonstiger Anlass','Verkehrsunfall','Ruhestörung','Diebstahl','Einbruch','Körperverletzung','Häusliche Gewalt','Vermisste Person','Verdächtige Person','Sachbeschädigung','Verkehrskontrolle','Amtshilfe'];
const personRoles = ['Beschuldigte Person','Tatverdächtige Person','Geschädigte Person','Zeugin / Zeuge','Anzeigende Person','Verletzte Person','Sorgeberechtigte Person','Sonstige Rolle'];
const evidenceStatus = ['Beschlagnahmt','Sichergestellt','Eingelagert','Zur Untersuchung versandt','Herausgegeben','Vernichtet'];
const documentTypes = ['Kurzbericht','Strafanzeige','Einsatzbericht','Zeugenvernehmung','Sicherstellungsprotokoll','Übersendungsschreiben','Abschlussbericht','Sonstiges Schreiben'];
const caseStatus = ['Offen','In Bearbeitung','Vorgelegt','Abgeschlossen'];
const state = { incidents: [], resources: [], cases: [], selectedId: null, selectedCaseId: null, filter: 'active', caseFilter: 'active' };
const auth = { accessToken: null, refreshToken: null, idToken: null, roles: [], expiresAt: 0, config: null, loggingOut: false };
let liveSocket;
let liveReconnectTimer;
let liveReloadTimer;
let simulationTimer;
let pendingTransmittedIncident;
let notificationAudioContext;

const transmittedIncidentTemplates = [
  {occasion:1,title:'Verkehrsunfall mit Sachschaden',description:'Zwei Fahrzeuge beteiligt. Die Unfallstelle ist noch nicht abgesichert.'},
  {occasion:2,title:'Ruhestörung durch Feier',description:'Mehrere Anrufende melden anhaltend laute Musik und Personen auf der Straße.'},
  {occasion:3,title:'Diebstahl aus Kraftfahrzeug',description:'Seitenscheibe eingeschlagen, Wertgegenstände aus dem Fahrzeug entwendet.'},
  {occasion:4,title:'Verdacht auf Wohnungseinbruch',description:'Eine aufgebrochene Terrassentür wurde gemeldet. Ob sich Personen im Objekt befinden, ist unklar.'},
  {occasion:5,title:'Körperverletzung im öffentlichen Raum',description:'Auseinandersetzung zwischen mehreren Personen. Rettungsdienst ist verständigt.'},
  {occasion:7,title:'Vermisste Person',description:'Eine hilfsbedürftige Person wurde zuletzt im Ortsbereich gesehen. Fahndungsmaßnahmen laufen.'},
  {occasion:8,title:'Verdächtige Person an Wohnhaus',description:'Eine unbekannte Person prüft wiederholt Türen und Fenster mehrerer Gebäude.'},
  {occasion:9,title:'Sachbeschädigung an öffentlicher Einrichtung',description:'Mehrere Beschädigungen wurden festgestellt. Tatverdächtige Personen sind nicht mehr vor Ort.'},
  {occasion:11,title:'Amtshilfe für Nachbarpräsidium',description:'Unterstützungsersuchen zur Überprüfung einer Anschrift und Feststellung anwesender Personen.'}
];
const transmittedLocations = ['Dorfstraße 18, Well','Gelderner Straße 42, Well','Am Bruch 7, Well','Kapellenweg 11, Well','Maasstraße 26, Well','Bahnhofstraße 9, Kevelaer','Markt 3, Geldern'];
const transmittingAuthorities = ['Polizeipräsidium Kleve','Polizeipräsidium Krefeld','Polizeipräsidium Duisburg','Leitstelle Kreis Kleve'];

const $ = selector => document.querySelector(selector);
const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]));
const formatDate = value => new Intl.DateTimeFormat('de-DE', {day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}).format(new Date(value));

async function api(path, options = {}) {
  await refreshAccessToken();
  const response = await fetch(path, { ...options, headers: {'Content-Type':'application/json',Authorization:`Bearer ${auth.accessToken}`, ...(options.headers || {})} });
  if (!response.ok) {
    let message = `Fehler ${response.status}`;
    try { const body = await response.json(); message = body.detail || body.title || message; } catch {}
    throw new Error(message);
  }
  return response.status === 204 || response.headers.get('content-length') === '0' ? null : response.json();
}

function base64Url(bytes) {
  return btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function sha256(value) { return crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)); }

function tokenPayload(token) {
  try {
    const encoded = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '=')));
  } catch { return {}; }
}

async function exchangeToken(parameters) {
  const endpoint = `${auth.config.authority}/protocol/openid-connect/token`;
  const response = await fetch(endpoint, {method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams(parameters)});
  if (!response.ok) throw new Error('Anmeldung bei Keycloak fehlgeschlagen.');
  const tokens = await response.json();
  auth.accessToken = tokens.access_token;
  auth.refreshToken = tokens.refresh_token || auth.refreshToken;
  auth.idToken = tokens.id_token || auth.idToken;
  auth.roles = tokenPayload(auth.accessToken).realm_access?.roles || [];
  $('#new-user')?.classList.toggle('hidden', !auth.roles.includes('user-admin'));
  auth.expiresAt = Date.now() + (tokens.expires_in * 1000);
}

function logout() {
  auth.loggingOut = true;
  clearTimeout(liveReconnectTimer);
  clearTimeout(liveReloadTimer);
  liveSocket?.close();
  const query = new URLSearchParams({client_id:auth.config.clientId,post_logout_redirect_uri:`${location.origin}${location.pathname}`});
  if (auth.idToken) query.set('id_token_hint', auth.idToken);
  auth.accessToken = null;
  auth.refreshToken = null;
  auth.idToken = null;
  auth.roles = [];
  auth.expiresAt = 0;
  sessionStorage.removeItem('leitweb-login-state');
  sessionStorage.removeItem('leitweb-pkce-verifier');
  location.assign(`${auth.config.authority}/protocol/openid-connect/logout?${query}`);
}

async function refreshAccessToken() {
  if (auth.accessToken && Date.now() < auth.expiresAt - 30000) return;
  if (!auth.refreshToken) return startLogin();
  try {
    await exchangeToken({grant_type:'refresh_token',client_id:auth.config.clientId,refresh_token:auth.refreshToken});
  } catch { startLogin(); }
}

async function startLogin() {
  const verifier = base64Url(crypto.getRandomValues(new Uint8Array(32)));
  const loginState = base64Url(crypto.getRandomValues(new Uint8Array(24)));
  sessionStorage.setItem('leitweb-pkce-verifier', verifier);
  sessionStorage.setItem('leitweb-login-state', loginState);
  const redirectUri = `${location.origin}${location.pathname}`;
  const supportsS256 = Boolean(globalThis.crypto?.subtle);
  const codeChallenge = supportsS256 ? base64Url(await sha256(verifier)) : verifier;
  const query = new URLSearchParams({client_id:auth.config.clientId,redirect_uri:redirectUri,response_type:'code',scope:'openid profile',state:loginState,code_challenge:codeChallenge,code_challenge_method:supportsS256?'S256':'plain'});
  location.assign(`${auth.config.authority}/protocol/openid-connect/auth?${query}`);
  return new Promise(() => {});
}

async function initializeAuthentication() {
  const response = await fetch('/app-config.json');
  if (!response.ok) throw new Error('Anwendungskonfiguration konnte nicht geladen werden.');
  auth.config = await response.json();
  const query = new URLSearchParams(location.search);
  const code = query.get('code');
  if (!code) return startLogin();
  const expectedState = sessionStorage.getItem('leitweb-login-state');
  const verifier = sessionStorage.getItem('leitweb-pkce-verifier');
  if (!expectedState || query.get('state') !== expectedState || !verifier) throw new Error('Ungültige Anmeldeantwort.');
  await exchangeToken({grant_type:'authorization_code',client_id:auth.config.clientId,code,redirect_uri:`${location.origin}${location.pathname}`,code_verifier:verifier});
  sessionStorage.removeItem('leitweb-login-state');
  sessionStorage.removeItem('leitweb-pkce-verifier');
  history.replaceState({}, document.title, location.pathname);
}

async function connectLiveUpdates() {
  if (auth.loggingOut) return;
  clearTimeout(liveReconnectTimer);
  try {
    await refreshAccessToken();
    if (!auth.accessToken || auth.loggingOut || liveSocket?.readyState === WebSocket.OPEN || liveSocket?.readyState === WebSocket.CONNECTING) return;
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    liveSocket = new WebSocket(`${protocol}//${location.host}/ws/updates?access_token=${encodeURIComponent(auth.accessToken)}`);
    liveSocket.onmessage = () => {
      clearTimeout(liveReloadTimer);
      liveReloadTimer = setTimeout(loadAll, 100);
    };
    liveSocket.onclose = () => {
      liveSocket = null;
      if (!auth.loggingOut) liveReconnectTimer = setTimeout(connectLiveUpdates, 2000);
    };
    liveSocket.onerror = () => liveSocket?.close();
  } catch {
    if (!auth.loggingOut) liveReconnectTimer = setTimeout(connectLiveUpdates, 2000);
  }
}

function randomItem(items) { return items[Math.floor(Math.random() * items.length)]; }

function enableNotificationAudio() {
  notificationAudioContext ??= new (window.AudioContext || window.webkitAudioContext)();
  if (notificationAudioContext.state === 'suspended') notificationAudioContext.resume();
}

function playTransmissionSound() {
  try {
    enableNotificationAudio();
    const start = notificationAudioContext.currentTime;
    const gain = notificationAudioContext.createGain();
    gain.gain.setValueAtTime(0.0001, start);
    gain.gain.exponentialRampToValueAtTime(0.07, start + 0.025);
    gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.55);
    gain.connect(notificationAudioContext.destination);
    [[659.25, 0], [783.99, 0.18]].forEach(([frequency, offset]) => {
      const oscillator = notificationAudioContext.createOscillator();
      oscillator.type = 'sine';
      oscillator.frequency.value = frequency;
      oscillator.connect(gain);
      oscillator.start(start + offset);
      oscillator.stop(start + offset + 0.32);
    });
  } catch { }
}

function createTransmittedIncident() {
  const template = randomItem(transmittedIncidentTemplates);
  const authority = randomItem(transmittingAuthorities);
  const suffix = `${Date.now().toString().slice(-7)}${Math.floor(Math.random() * 90 + 10)}`;
  return {
    referenceNumber: `FREMD-${new Date().getFullYear()}-${suffix}`,
    title: template.title,
    occasion: template.occasion,
    location: randomItem(transmittedLocations),
    description: `Übermittelt durch ${authority}. ${template.description}`
  };
}

function scheduleTransmittedIncident() {
  clearTimeout(simulationTimer);
  const frequency = $('#incident-simulation').value;
  if (frequency === 'off' || pendingTransmittedIncident) return;
  const limits = frequency === 'high' ? [120000, 240000] : [420000, 780000];
  simulationTimer = setTimeout(() => {
    pendingTransmittedIncident = createTransmittedIncident();
    $('#transmission-alert').classList.remove('hidden');
    playTransmissionSound();
  }, limits[0] + Math.random() * (limits[1] - limits[0]));
}

function initializeIncidentSimulation() {
  const saved = localStorage.getItem('leitweb-incident-simulation');
  $('#incident-simulation').value = ['off','low','high'].includes(saved) ? saved : 'off';
  scheduleTransmittedIncident();
}

function openTransmittedIncident() {
  if (!pendingTransmittedIncident) return;
  const incident = pendingTransmittedIncident;
  pendingTransmittedIncident = null;
  $('#transmission-alert').classList.add('hidden');
  showView('incidents');
  openIncidentDialog();
  const form = $('#incident-form');
  form.elements.referenceNumber.value = incident.referenceNumber;
  form.elements.title.value = incident.title;
  form.elements.occasion.value = incident.occasion;
  form.elements.location.value = incident.location;
  form.elements.description.value = incident.description;
  $('#incident-dialog-title').textContent = 'Übermittelten Einsatz übernehmen';
  scheduleTransmittedIncident();
}

async function loadAll() {
  try {
    [state.incidents, state.resources, state.cases] = await Promise.all([
      api(`/api/v1/incidents?organizationId=${organizationId}`),
      api(`/api/v1/resources?organizationId=${organizationId}`),
      api(`/api/v1/cases?organizationId=${organizationId}`)
    ]);
    renderIncidents(); renderStatusBoard(); renderCases();
    if (state.selectedId) await selectIncident(state.selectedId);
    if (state.selectedCaseId) await selectCase(state.selectedCaseId);
  } catch (error) { toast(error.message, true); }
}

function renderIncidents() {
  const active = state.incidents.filter(i => i.status < 3).length;
  const dispatched = state.incidents.filter(i => i.status === 1).length;
  const available = state.resources.filter(r => r.status === 1).length;
  $('#incident-stats').innerHTML = [
    [active, 'Aktive Einsätze'], [dispatched, 'Disponiert'], [available, 'Einsatzmittel verfügbar'], [state.resources.length, 'Einsatzmittel gesamt']
  ].map(([n,label]) => `<div class="stat"><strong>${n}</strong><span>${label}</span></div>`).join('');
  const visible = state.filter === 'active' ? state.incidents.filter(i => i.status < 3) : state.incidents;
  $('#incident-list').innerHTML = visible.length ? visible.map(i => `
    <article class="incident-row ${state.selectedId === i.id ? 'selected' : ''}" data-id="${i.id}" tabindex="0">
      <span class="priority"></span><div class="incident-main"><strong>${escapeHtml(i.title)}</strong>
      <div class="incident-meta"><span>${escapeHtml(i.referenceNumber)}</span><span>⌖ ${escapeHtml(i.location)}</span><span>${formatDate(i.createdAt)}</span></div></div>
      <div class="incident-side"><span class="badge s${i.status}">${incidentStatus[i.status]}</span><span class="count">${i.assignedResourceCount} Mittel</span></div>
    </article>`).join('') : '<div class="empty"><h3>Keine Einsätze</h3><p>Für diesen Filter liegen keine Vorgänge vor.</p></div>';
  document.querySelectorAll('.incident-row').forEach(row => {
    row.onclick = () => selectIncident(row.dataset.id);
    row.onkeydown = e => { if (e.key === 'Enter') selectIncident(row.dataset.id); };
  });
}

async function selectIncident(id) {
  state.selectedId = id; renderIncidents();
  try {
    const i = await api(`/api/v1/incidents/${id}`);
    const available = state.resources.filter(r => !i.assignedResources.some(a => a.id === r.id));
    $('#incident-detail').innerHTML = `<div class="detail-content">
      <div class="detail-top"><div><p class="eyebrow">${escapeHtml(i.referenceNumber)} · ${occasions[i.occasion]}</p><h2>${escapeHtml(i.title)}</h2></div><div><span class="badge s${i.status}">${incidentStatus[i.status]}</span><button class="icon-button" id="edit-incident" title="Einsatz bearbeiten">✎</button></div></div>
      <p class="detail-location">⌖ ${escapeHtml(i.location)}</p><div class="description">${escapeHtml(i.description) || 'Keine Lagebeschreibung vorhanden.'}</div>
      <h3 class="section-title">Einsatzstatus</h3><select id="incident-status">${incidentStatus.map((s,n)=>`<option value="${n}" ${n===i.status?'selected':''}>${s}</option>`).join('')}</select>
      <h3 class="section-title">Disponierte Einsatzmittel</h3>
      <div>${i.assignedResources.length ? i.assignedResources.map(r=>`<div class="assignment"><div><strong>${escapeHtml(r.callSign)}</strong><small>${escapeHtml(r.name)} · ${resourceStatus[r.status]}</small></div><button class="icon-button unassign" data-id="${r.id}" title="Einsatzmittel lösen">×</button></div>`).join('') : '<p class="detail-location">Noch keine Einsatzmittel disponiert.</p>'}</div>
      <div class="inline-control"><select id="assign-resource"><option value="">Einsatzmittel auswählen …</option>${available.map(r=>`<option value="${r.id}">${escapeHtml(r.callSign)} – ${escapeHtml(r.name)} (${resourceStatus[r.status]})</option>`).join('')}</select><button class="primary" id="assign-button">Zuweisen</button></div>
      <h3 class="section-title">Statusverlauf</h3><div class="timeline">${i.statusHistory.map(h=>`<div class="timeline-item"><strong>${incidentStatus[h.status]}</strong><small>${formatDate(h.changedAt)} · ${escapeHtml(h.changedBy)}</small></div>`).join('')}</div>
      <button class="primary" id="make-case">${state.cases.some(c=>c.incidentId===id)?'Fallakte öffnen':'Aus Einsatz einen Fall machen'}</button>
    </div>`;
    $('#incident-status').onchange = async e => { await api(`/api/v1/incidents/${id}/status`, {method:'PUT',body:JSON.stringify({status:+e.target.value})}); toast('Einsatzstatus aktualisiert'); await loadAll(); };
    $('#edit-incident').onclick = () => openIncidentDialog(i);
    $('#make-case').onclick = () => { const existing=state.cases.find(c=>c.incidentId===id); if(existing){showView('cases');selectCase(existing.id);}else openCaseDialog(i); };
    $('#assign-button').onclick = async () => { const resourceId=$('#assign-resource').value; if(!resourceId) return toast('Bitte ein Einsatzmittel auswählen', true); await api(`/api/v1/incidents/${id}/resources/${resourceId}`,{method:'POST'}); toast('Einsatzmittel disponiert'); await loadAll(); };
    document.querySelectorAll('.unassign').forEach(b => b.onclick = async () => { await api(`/api/v1/incidents/${id}/resources/${b.dataset.id}`,{method:'DELETE'}); toast('Einsatzmittel gelöst'); await loadAll(); });
  } catch (error) { toast(error.message, true); }
}

function renderStatusBoard(){
  $('#status-board').innerHTML=resourceStatus.map((label,status)=>{const items=state.resources.filter(r=>r.status===status);return `<section class="status-column status-${status}"><header><span>${label}</span><strong>${items.length}</strong></header><div>${items.map(r=>`<article class="status-unit"><strong>${escapeHtml(r.callSign)}</strong><small>${escapeHtml(r.name)}</small><div class="status-unit-actions"><label><span>Status</span><select data-resource-status="${r.id}" aria-label="Status von ${escapeHtml(r.callSign)} ändern">${resourceStatus.map((name,value)=>`<option value="${value}" ${value===r.status?'selected':''}>${name}</option>`).join('')}</select></label><button type="button" class="status-edit" data-edit-resource="${r.id}" title="${escapeHtml(r.callSign)} bearbeiten" aria-label="${escapeHtml(r.callSign)} bearbeiten">✎</button></div></article>`).join('')||'<p>Keine Einsatzmittel</p>'}</div></section>`;}).join('');
  document.querySelectorAll('[data-resource-status]').forEach(select=>select.onchange=async e=>{
    const resource=state.resources.find(r=>r.id===e.target.dataset.resourceStatus);
    if(!resource)return;
    e.target.disabled=true;
    try{await api(`/api/v1/resources/${resource.id}`,{method:'PUT',body:JSON.stringify({callSign:resource.callSign,name:resource.name,status:+e.target.value})});toast('Einsatzmittelstatus aktualisiert');await loadAll();}
    catch(error){toast(error.message,true);await loadAll();}
  });
  document.querySelectorAll('[data-edit-resource]').forEach(button=>button.onclick=()=>openResourceDialog(state.resources.find(r=>r.id===button.dataset.editResource)));
}

function renderCases(){
  const visible=state.caseFilter==='all'?state.cases:state.caseFilter==='active'?state.cases.filter(c=>c.status<3):state.cases.filter(c=>c.status===+state.caseFilter);
  $('#case-list').innerHTML=visible.length?visible.map(c=>`<article class="incident-row ${state.selectedCaseId===c.id?'selected':''}" data-case-id="${c.id}"><span class="priority"></span><div class="incident-main"><strong>${escapeHtml(c.subject)}</strong><div class="incident-meta"><span>${escapeHtml(c.fileNumber)}</span><span>${c.personCount} Personen</span><span>${c.evidenceCount} Asservate</span></div></div><div class="incident-side"><span class="badge s${c.status}">${caseStatus[c.status]}</span><span class="count">${c.documentCount} Schreiben</span></div></article>`).join(''):'<div class="empty"><h3>Keine passenden Fälle</h3><p>Für den gewählten Status liegen keine Fallakten vor.</p></div>';
  document.querySelectorAll('[data-case-id]').forEach(r=>r.onclick=()=>selectCase(r.dataset.caseId));
}

async function selectCase(id){
  state.selectedCaseId=id;renderCases();
  try{
    const c=await api(`/api/v1/cases/${id}`);
    $('#case-detail').innerHTML=`<div class="detail-content"><div class="detail-top"><div><p class="eyebrow">${escapeHtml(c.fileNumber)}</p><h2>${escapeHtml(c.subject)}</h2></div><span class="badge s${c.status}">${caseStatus[c.status]}</span></div><h3 class="section-title">Fallstatus</h3><select id="case-status">${caseStatus.map((label,status)=>`<option value="${status}" ${status===c.status?'selected':''}>${label}</option>`).join('')}</select>
    <h3 class="section-title">Beteiligte Personen</h3>${c.persons.map(p=>`<div class="assignment"><div><strong>${escapeHtml(p.lastName)}, ${escapeHtml(p.firstName)}</strong><small>${personRoles[p.role]}${p.dateOfBirth?' · * '+p.dateOfBirth:''}</small></div></div>`).join('')||'<p class="detail-location">Keine Personen erfasst.</p>'}<button class="secondary" id="add-person">+ Person</button>
    <h3 class="section-title">Asservate</h3>${c.evidence.map(e=>`<div class="assignment"><div><strong>${escapeHtml(e.evidenceNumber)} · ${escapeHtml(e.description)}</strong><small>${evidenceStatus[e.status]} · ${escapeHtml(e.storageLocation)}</small></div></div>`).join('')||'<p class="detail-location">Keine Asservate erfasst.</p>'}<button class="secondary" id="add-evidence">+ Asservat</button>
    <h3 class="section-title">Schriftstücke und Abverfügungen</h3>${c.documents.map(d=>`<div class="case-document"><strong>${documentTypes[d.type]} · ${escapeHtml(d.title)}</strong><pre>${escapeHtml(d.content)}</pre><small>${d.dispatches.length?d.dispatches.map(v=>`Abverfügt an ${escapeHtml(v.recipient)} · ${formatDate(v.dispatchedAt)}`).join('<br>'):'Noch nicht abverfügt'}</small><button class="secondary dispatch-document" data-document-id="${d.id}">Abverfügen</button></div>`).join('')||'<p class="detail-location">Keine Schreiben vorhanden.</p>'}<button class="primary" id="add-document">+ Schreiben erstellen</button></div>`;
    $('#case-status').onchange=async e=>{try{await api(`/api/v1/cases/${id}/status`,{method:'PUT',body:JSON.stringify({status:+e.target.value})});toast('Fallstatus aktualisiert');await loadAll();await selectCase(id);}catch(error){toast(error.message,true);}};
    $('#add-person').onclick=()=>openRelatedDialog('person',id); $('#add-evidence').onclick=()=>openRelatedDialog('evidence',id); $('#add-document').onclick=()=>openRelatedDialog('document',id);
    document.querySelectorAll('.dispatch-document').forEach(b=>b.onclick=()=>{const f=$('#dispatch-form');f.reset();f.elements.caseId.value=id;f.elements.documentId.value=b.dataset.documentId;$('#dispatch-dialog').showModal();});
  }catch(error){toast(error.message,true);}
}

function openCaseDialog(incident){const f=$('#case-form');f.reset();f.elements.incidentId.value=incident.id;f.elements.fileNumber.value=incident.referenceNumber.replace('-E-','-A-');f.elements.subject.value=incident.title;$('#case-dialog').showModal();}
function openRelatedDialog(type,caseId){const f=$(`#${type}-form`);f.reset();f.elements.caseId.value=caseId;$(`#${type}-dialog`).showModal();}

function toast(message, error=false) { const el=$('#toast'); el.textContent=message; el.style.background=error?'#8f2924':''; el.classList.add('show'); setTimeout(()=>el.classList.remove('show'),2600); }
function showView(name) { document.querySelectorAll('.view').forEach(v=>v.classList.add('hidden')); $(`#${name}-view`).classList.remove('hidden'); document.querySelectorAll('.nav-item[data-view]').forEach(n=>n.classList.toggle('active',n.dataset.view===name)); }

document.querySelectorAll('.nav-item[data-view]').forEach(n => n.onclick=async()=>{showView(n.dataset.view);if(n.dataset.view==='cases')await loadAll();});
document.querySelectorAll('.filter-chip').forEach(b => b.onclick=()=>{state.filter=b.dataset.filter;document.querySelectorAll('.filter-chip').forEach(x=>x.classList.toggle('active',x===b));renderIncidents();});
$('#case-filter').onchange=e=>{state.caseFilter=e.target.value;renderCases();};
function openIncidentDialog(incident=null) { const f=$('#incident-form'); f.reset(); f.elements.id.value=incident?.id||''; f.elements.referenceNumber.value=incident?.referenceNumber||`DPW-E-${new Date().getFullYear()}-`; f.elements.referenceNumber.disabled=!!incident; f.elements.title.value=incident?.title||''; f.elements.location.value=incident?.location||''; f.elements.description.value=incident?.description||''; f.elements.occasion.value=incident?.occasion??2; $('#incident-dialog-title').textContent=incident?'Einsatz bearbeiten':'Neuer Einsatz'; $('#incident-submit').textContent=incident?'Änderungen speichern':'Einsatz eröffnen'; $('#incident-dialog').showModal(); }
function openResourceDialog(resource=null) { const f=$('#resource-form'); f.reset(); f.elements.id.value=resource?.id||''; f.elements.callSign.value=resource?.callSign||''; f.elements.name.value=resource?.name||''; $('#resource-dialog-title').textContent=resource?'Einsatzmittel bearbeiten':'Einsatzmittel anlegen'; $('#resource-dialog').showModal(); }
$('#new-incident').onclick=()=>openIncidentDialog(); $('#new-resource').onclick=()=>openResourceDialog();
$('#new-user').onclick=()=>{ $('#user-form').reset(); $('#user-dialog').showModal(); };
$('#logout').onclick=logout;
$('#incident-simulation').onchange=e=>{localStorage.setItem('leitweb-incident-simulation',e.target.value);scheduleTransmittedIncident();};
$('#transmission-alert').onclick=openTransmittedIncident;
document.addEventListener('pointerdown',enableNotificationAudio,{once:true});
document.addEventListener('keydown',enableNotificationAudio,{once:true});
document.querySelectorAll('.close-dialog').forEach(b=>b.onclick=()=>b.closest('dialog').close());
$('#incident-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const id=data.id;delete data.id;data.occasion=+data.occasion;try{if(id)await api(`/api/v1/incidents/${id}`,{method:'PUT',body:JSON.stringify(data)});else await api('/api/v1/incidents',{method:'POST',body:JSON.stringify({...data,organizationId})});e.target.reset();$('#incident-dialog').close();toast(id?'Einsatz aktualisiert':'Einsatz eröffnet');await loadAll();}catch(error){toast(error.message,true);}};
$('#resource-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const id=data.id;delete data.id;try{if(id){const current=state.resources.find(r=>r.id===id);await api(`/api/v1/resources/${id}`,{method:'PUT',body:JSON.stringify({...data,status:current.status})});}else await api('/api/v1/resources',{method:'POST',body:JSON.stringify({...data,organizationId})});e.target.reset();$('#resource-dialog').close();toast(id?'Einsatzmittel aktualisiert':'Einsatzmittel angelegt');await loadAll();}catch(error){toast(error.message,true);}};
$('#case-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const incidentId=data.incidentId;delete data.incidentId;try{await api(`/api/v1/cases/from-incident/${incidentId}`,{method:'POST',body:JSON.stringify(data)});$('#case-dialog').close();toast('Fallakte angelegt');await loadAll();showView('cases');const c=state.cases.find(x=>x.incidentId===incidentId);if(c)selectCase(c.id);}catch(error){toast(error.message,true);}};
$('#person-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId;delete data.caseId;data.role=+data.role;data.dateOfBirth=data.dateOfBirth||null;try{await api(`/api/v1/cases/${caseId}/persons`,{method:'POST',body:JSON.stringify(data)});$('#person-dialog').close();toast('Person zur Fallakte übernommen');await loadAll();await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#evidence-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId;delete data.caseId;data.status=+data.status;try{await api(`/api/v1/cases/${caseId}/evidence`,{method:'POST',body:JSON.stringify(data)});$('#evidence-dialog').close();toast('Asservat angelegt');await loadAll();await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#document-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId;delete data.caseId;data.type=+data.type;try{await api(`/api/v1/cases/${caseId}/documents`,{method:'POST',body:JSON.stringify(data)});$('#document-dialog').close();toast('Schreiben erstellt');await loadAll();await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#dispatch-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId,documentId=data.documentId;delete data.caseId;delete data.documentId;try{await api(`/api/v1/cases/${caseId}/documents/${documentId}/dispatches`,{method:'POST',body:JSON.stringify(data)});$('#dispatch-dialog').close();toast('Schreiben abverfügt');await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#user-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));try{await api('/api/v1/users',{method:'POST',body:JSON.stringify(data)});e.target.reset();$('#user-dialog').close();toast('Benutzer wurde in Keycloak angelegt');}catch(error){toast(error.message,true);}};
setInterval(()=>$('#clock').textContent=new Date().toLocaleTimeString('de-DE',{hour:'2-digit',minute:'2-digit',second:'2-digit'}),1000);
let addressTimer;
$('#incident-form').elements.location.addEventListener('input',e=>{clearTimeout(addressTimer);const query=e.target.value.trim();if(query.length<2)return;addressTimer=setTimeout(async()=>{try{const addresses=await api(`/api/v1/addresses/search?query=${encodeURIComponent(query)}&limit=40`);$('#address-suggestions').innerHTML=addresses.map(a=>`<option value="${escapeHtml(a.displayName)}"></option>`).join('');}catch{}},250);});
initializeAuthentication().then(async () => { initializeIncidentSimulation(); await loadAll(); connectLiveUpdates(); }).catch(error => toast(error.message, true));
