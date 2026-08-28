const organizationId = '11111111-1111-1111-1111-111111111111';
const incidentStatus = ['Offen', 'Disponiert', 'In Bearbeitung', 'Abgeschlossen', 'Storniert'];
const resourceStatus = ['Nicht verfügbar', 'Einsatzbereit', 'Disponiert', 'An Einsatzstelle'];
const state = { incidents: [], resources: [], selectedId: null, filter: 'active' };

const $ = selector => document.querySelector(selector);
const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]));
const formatDate = value => new Intl.DateTimeFormat('de-DE', {day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}).format(new Date(value));

async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: {'Content-Type':'application/json', ...(options.headers || {})} });
  if (!response.ok) {
    let message = `Fehler ${response.status}`;
    try { const body = await response.json(); message = body.detail || body.title || message; } catch {}
    throw new Error(message);
  }
  return response.status === 204 || response.headers.get('content-length') === '0' ? null : response.json();
}

async function loadAll() {
  try {
    [state.incidents, state.resources] = await Promise.all([
      api(`/api/v1/incidents?organizationId=${organizationId}`),
      api(`/api/v1/resources?organizationId=${organizationId}`)
    ]);
    renderIncidents(); renderResources();
    if (state.selectedId) await selectIncident(state.selectedId);
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
      <div class="detail-top"><div><p class="eyebrow">${escapeHtml(i.referenceNumber)}</p><h2>${escapeHtml(i.title)}</h2></div><div><span class="badge s${i.status}">${incidentStatus[i.status]}</span><button class="icon-button" id="edit-incident" title="Einsatz bearbeiten">✎</button></div></div>
      <p class="detail-location">⌖ ${escapeHtml(i.location)}</p><div class="description">${escapeHtml(i.description) || 'Keine Lagebeschreibung vorhanden.'}</div>
      <h3 class="section-title">Einsatzstatus</h3><select id="incident-status">${incidentStatus.map((s,n)=>`<option value="${n}" ${n===i.status?'selected':''}>${s}</option>`).join('')}</select>
      <h3 class="section-title">Disponierte Einsatzmittel</h3>
      <div>${i.assignedResources.length ? i.assignedResources.map(r=>`<div class="assignment"><div><strong>${escapeHtml(r.callSign)}</strong><small>${escapeHtml(r.name)} · ${resourceStatus[r.status]}</small></div><button class="icon-button unassign" data-id="${r.id}" title="Einsatzmittel lösen">×</button></div>`).join('') : '<p class="detail-location">Noch keine Einsatzmittel disponiert.</p>'}</div>
      <div class="inline-control"><select id="assign-resource"><option value="">Einsatzmittel auswählen …</option>${available.map(r=>`<option value="${r.id}">${escapeHtml(r.callSign)} – ${escapeHtml(r.name)} (${resourceStatus[r.status]})</option>`).join('')}</select><button class="primary" id="assign-button">Zuweisen</button></div>
      <h3 class="section-title">Statusverlauf</h3><div class="timeline">${i.statusHistory.map(h=>`<div class="timeline-item"><strong>${incidentStatus[h.status]}</strong><small>${formatDate(h.changedAt)} · ${escapeHtml(h.changedBy)}</small></div>`).join('')}</div>
    </div>`;
    $('#incident-status').onchange = async e => { await api(`/api/v1/incidents/${id}/status`, {method:'PUT',body:JSON.stringify({status:+e.target.value})}); toast('Einsatzstatus aktualisiert'); await loadAll(); };
    $('#edit-incident').onclick = () => openIncidentDialog(i);
    $('#assign-button').onclick = async () => { const resourceId=$('#assign-resource').value; if(!resourceId) return toast('Bitte ein Einsatzmittel auswählen', true); await api(`/api/v1/incidents/${id}/resources/${resourceId}`,{method:'POST'}); toast('Einsatzmittel disponiert'); await loadAll(); };
    document.querySelectorAll('.unassign').forEach(b => b.onclick = async () => { await api(`/api/v1/incidents/${id}/resources/${b.dataset.id}`,{method:'DELETE'}); toast('Einsatzmittel gelöst'); await loadAll(); });
  } catch (error) { toast(error.message, true); }
}

function renderResources() {
  $('#resource-list').innerHTML = state.resources.length ? state.resources.map(r => `<article class="resource-card">
    <div class="resource-card-head"><h3>${escapeHtml(r.callSign)}</h3><span class="badge s${r.status === 1 ? 3 : r.status === 0 ? 4 : 1}">${resourceStatus[r.status]}</span></div>
    <p>${escapeHtml(r.name)}</p><label>Taktischen Status ändern<select class="resource-status" data-id="${r.id}" data-call="${escapeHtml(r.callSign)}" data-name="${escapeHtml(r.name)}">${resourceStatus.map((s,n)=>`<option value="${n}" ${n===r.status?'selected':''}>${s}</option>`).join('')}</select></label><button class="secondary edit-resource" data-id="${r.id}">Stammdaten bearbeiten</button>
  </article>`).join('') : '<div class="empty"><h3>Keine Einsatzmittel</h3></div>';
  document.querySelectorAll('.resource-status').forEach(select => select.onchange = async e => {
    await api(`/api/v1/resources/${e.target.dataset.id}`, {method:'PUT', body:JSON.stringify({callSign:e.target.dataset.call,name:e.target.dataset.name,status:+e.target.value})});
    toast('Einsatzmittelstatus aktualisiert'); await loadAll();
  });
  document.querySelectorAll('.edit-resource').forEach(button => button.onclick = () => openResourceDialog(state.resources.find(r => r.id === button.dataset.id)));
}

function toast(message, error=false) { const el=$('#toast'); el.textContent=message; el.style.background=error?'#8f2924':''; el.classList.add('show'); setTimeout(()=>el.classList.remove('show'),2600); }
function showView(name) { document.querySelectorAll('.view').forEach(v=>v.classList.add('hidden')); $(`#${name}-view`).classList.remove('hidden'); document.querySelectorAll('.nav-item[data-view]').forEach(n=>n.classList.toggle('active',n.dataset.view===name)); }

document.querySelectorAll('.nav-item[data-view]').forEach(n => n.onclick=()=>showView(n.dataset.view));
document.querySelectorAll('.filter-chip').forEach(b => b.onclick=()=>{state.filter=b.dataset.filter;document.querySelectorAll('.filter-chip').forEach(x=>x.classList.toggle('active',x===b));renderIncidents();});
function openIncidentDialog(incident=null) { const f=$('#incident-form'); f.reset(); f.elements.id.value=incident?.id||''; f.elements.referenceNumber.value=incident?.referenceNumber||`E-${new Date().getFullYear()}-`; f.elements.referenceNumber.disabled=!!incident; f.elements.title.value=incident?.title||''; f.elements.location.value=incident?.location||''; f.elements.description.value=incident?.description||''; $('#incident-dialog-title').textContent=incident?'Einsatz bearbeiten':'Neuer Einsatz'; $('#incident-submit').textContent=incident?'Änderungen speichern':'Einsatz eröffnen'; $('#incident-dialog').showModal(); }
function openResourceDialog(resource=null) { const f=$('#resource-form'); f.reset(); f.elements.id.value=resource?.id||''; f.elements.callSign.value=resource?.callSign||''; f.elements.name.value=resource?.name||''; $('#resource-dialog-title').textContent=resource?'Einsatzmittel bearbeiten':'Einsatzmittel anlegen'; $('#resource-dialog').showModal(); }
$('#new-incident').onclick=()=>openIncidentDialog(); $('#new-resource').onclick=()=>openResourceDialog();
document.querySelectorAll('.close-dialog').forEach(b=>b.onclick=()=>b.closest('dialog').close());
$('#incident-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const id=data.id;delete data.id;try{if(id)await api(`/api/v1/incidents/${id}`,{method:'PUT',body:JSON.stringify(data)});else await api('/api/v1/incidents',{method:'POST',body:JSON.stringify({...data,organizationId})});e.target.reset();$('#incident-dialog').close();toast(id?'Einsatz aktualisiert':'Einsatz eröffnet');await loadAll();}catch(error){toast(error.message,true);}};
$('#resource-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const id=data.id;delete data.id;try{if(id){const current=state.resources.find(r=>r.id===id);await api(`/api/v1/resources/${id}`,{method:'PUT',body:JSON.stringify({...data,status:current.status})});}else await api('/api/v1/resources',{method:'POST',body:JSON.stringify({...data,organizationId})});e.target.reset();$('#resource-dialog').close();toast(id?'Einsatzmittel aktualisiert':'Einsatzmittel angelegt');await loadAll();}catch(error){toast(error.message,true);}};
setInterval(()=>$('#clock').textContent=new Date().toLocaleTimeString('de-DE',{hour:'2-digit',minute:'2-digit',second:'2-digit'}),1000);
loadAll();
