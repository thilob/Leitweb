const organizationId = '11111111-1111-1111-1111-111111111111';
const incidentStatus = ['Offen', 'Disponiert', 'In Bearbeitung', 'Abgeschlossen', 'Storniert'];
const resourceStatus = ['Nicht verfügbar', 'Einsatzbereit', 'Disponiert', 'An Einsatzstelle'];
const occasions = ['Sonstiger Anlass','Verkehrsunfall','Ruhestörung','Diebstahl','Einbruch','Körperverletzung','Häusliche Gewalt','Vermisste Person','Verdächtige Person','Sachbeschädigung','Verkehrskontrolle','Amtshilfe'];
const personRoles = ['Beschuldigte Person','Tatverdächtige Person','Geschädigte Person','Zeugin / Zeuge','Anzeigende Person','Verletzte Person','Sorgeberechtigte Person','Sonstige Rolle'];
const evidenceStatus = ['Beschlagnahmt','Sichergestellt','Eingelagert','Zur Untersuchung versandt','Herausgegeben','Vernichtet'];
const documentTypes = ['Kurzbericht','Strafanzeige','Einsatzbericht','Zeugenvernehmung','Sicherstellungsprotokoll','Übersendungsschreiben','Abschlussbericht','Sonstiges Schreiben'];
const caseStatus = ['Offen','In Bearbeitung','Vorgelegt','Abgeschlossen'];
const state = { incidents: [], resources: [], cases: [], selectedId: null, selectedCaseId: null, filter: 'active' };

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
    [state.incidents, state.resources, state.cases] = await Promise.all([
      api(`/api/v1/incidents?organizationId=${organizationId}`),
      api(`/api/v1/resources?organizationId=${organizationId}`),
      api(`/api/v1/cases?organizationId=${organizationId}`)
    ]);
    renderIncidents(); renderResources(); renderStatusBoard(); renderCases();
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

function renderStatusBoard(){
  $('#status-board').innerHTML=resourceStatus.map((label,status)=>{const items=state.resources.filter(r=>r.status===status);return `<section class="status-column status-${status}"><header><span>${label}</span><strong>${items.length}</strong></header><div>${items.map(r=>`<article class="status-unit"><strong>${escapeHtml(r.callSign)}</strong><small>${escapeHtml(r.name)}</small></article>`).join('')||'<p>Keine Einsatzmittel</p>'}</div></section>`;}).join('');
}

function renderCases(){
  $('#case-list').innerHTML=state.cases.length?state.cases.map(c=>`<article class="incident-row ${state.selectedCaseId===c.id?'selected':''}" data-case-id="${c.id}"><span class="priority"></span><div class="incident-main"><strong>${escapeHtml(c.subject)}</strong><div class="incident-meta"><span>${escapeHtml(c.fileNumber)}</span><span>${c.personCount} Personen</span><span>${c.evidenceCount} Asservate</span></div></div><div class="incident-side"><span class="badge s${c.status}">${caseStatus[c.status]}</span><span class="count">${c.documentCount} Schreiben</span></div></article>`).join(''):'<div class="empty"><h3>Noch keine Fälle</h3><p>Ein Fall wird aus einem Einsatz heraus angelegt.</p></div>';
  document.querySelectorAll('[data-case-id]').forEach(r=>r.onclick=()=>selectCase(r.dataset.caseId));
}

async function selectCase(id){
  state.selectedCaseId=id;renderCases();
  try{
    const c=await api(`/api/v1/cases/${id}`);
    $('#case-detail').innerHTML=`<div class="detail-content"><div class="detail-top"><div><p class="eyebrow">${escapeHtml(c.fileNumber)}</p><h2>${escapeHtml(c.subject)}</h2></div><span class="badge s${c.status}">${caseStatus[c.status]}</span></div>
    <h3 class="section-title">Beteiligte Personen</h3>${c.persons.map(p=>`<div class="assignment"><div><strong>${escapeHtml(p.lastName)}, ${escapeHtml(p.firstName)}</strong><small>${personRoles[p.role]}${p.dateOfBirth?' · * '+p.dateOfBirth:''}</small></div></div>`).join('')||'<p class="detail-location">Keine Personen erfasst.</p>'}<button class="secondary" id="add-person">+ Person</button>
    <h3 class="section-title">Asservate</h3>${c.evidence.map(e=>`<div class="assignment"><div><strong>${escapeHtml(e.evidenceNumber)} · ${escapeHtml(e.description)}</strong><small>${evidenceStatus[e.status]} · ${escapeHtml(e.storageLocation)}</small></div></div>`).join('')||'<p class="detail-location">Keine Asservate erfasst.</p>'}<button class="secondary" id="add-evidence">+ Asservat</button>
    <h3 class="section-title">Schriftstücke und Abverfügungen</h3>${c.documents.map(d=>`<div class="case-document"><strong>${documentTypes[d.type]} · ${escapeHtml(d.title)}</strong><pre>${escapeHtml(d.content)}</pre><small>${d.dispatches.length?d.dispatches.map(v=>`Abverfügt an ${escapeHtml(v.recipient)} · ${formatDate(v.dispatchedAt)}`).join('<br>'):'Noch nicht abverfügt'}</small><button class="secondary dispatch-document" data-document-id="${d.id}">Abverfügen</button></div>`).join('')||'<p class="detail-location">Keine Schreiben vorhanden.</p>'}<button class="primary" id="add-document">+ Schreiben erstellen</button></div>`;
    $('#add-person').onclick=()=>openRelatedDialog('person',id); $('#add-evidence').onclick=()=>openRelatedDialog('evidence',id); $('#add-document').onclick=()=>openRelatedDialog('document',id);
    document.querySelectorAll('.dispatch-document').forEach(b=>b.onclick=()=>{const f=$('#dispatch-form');f.reset();f.elements.caseId.value=id;f.elements.documentId.value=b.dataset.documentId;$('#dispatch-dialog').showModal();});
  }catch(error){toast(error.message,true);}
}

function openCaseDialog(incident){const f=$('#case-form');f.reset();f.elements.incidentId.value=incident.id;f.elements.fileNumber.value=`DPW-${new Date().getFullYear()}-`;f.elements.subject.value=incident.title;$('#case-dialog').showModal();}
function openRelatedDialog(type,caseId){const f=$(`#${type}-form`);f.reset();f.elements.caseId.value=caseId;$(`#${type}-dialog`).showModal();}

function toast(message, error=false) { const el=$('#toast'); el.textContent=message; el.style.background=error?'#8f2924':''; el.classList.add('show'); setTimeout(()=>el.classList.remove('show'),2600); }
function showView(name) { document.querySelectorAll('.view').forEach(v=>v.classList.add('hidden')); $(`#${name}-view`).classList.remove('hidden'); document.querySelectorAll('.nav-item[data-view]').forEach(n=>n.classList.toggle('active',n.dataset.view===name)); }

document.querySelectorAll('.nav-item[data-view]').forEach(n => n.onclick=()=>showView(n.dataset.view));
document.querySelectorAll('.filter-chip').forEach(b => b.onclick=()=>{state.filter=b.dataset.filter;document.querySelectorAll('.filter-chip').forEach(x=>x.classList.toggle('active',x===b));renderIncidents();});
function openIncidentDialog(incident=null) { const f=$('#incident-form'); f.reset(); f.elements.id.value=incident?.id||''; f.elements.referenceNumber.value=incident?.referenceNumber||`DPW-E-${new Date().getFullYear()}-`; f.elements.referenceNumber.disabled=!!incident; f.elements.title.value=incident?.title||''; f.elements.location.value=incident?.location||''; f.elements.description.value=incident?.description||''; f.elements.occasion.value=incident?.occasion??2; $('#incident-dialog-title').textContent=incident?'Einsatz bearbeiten':'Neuer Einsatz'; $('#incident-submit').textContent=incident?'Änderungen speichern':'Einsatz eröffnen'; $('#incident-dialog').showModal(); }
function openResourceDialog(resource=null) { const f=$('#resource-form'); f.reset(); f.elements.id.value=resource?.id||''; f.elements.callSign.value=resource?.callSign||''; f.elements.name.value=resource?.name||''; $('#resource-dialog-title').textContent=resource?'Einsatzmittel bearbeiten':'Einsatzmittel anlegen'; $('#resource-dialog').showModal(); }
$('#new-incident').onclick=()=>openIncidentDialog(); $('#new-resource').onclick=()=>openResourceDialog();
document.querySelectorAll('.close-dialog').forEach(b=>b.onclick=()=>b.closest('dialog').close());
$('#incident-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const id=data.id;delete data.id;data.occasion=+data.occasion;try{if(id)await api(`/api/v1/incidents/${id}`,{method:'PUT',body:JSON.stringify(data)});else await api('/api/v1/incidents',{method:'POST',body:JSON.stringify({...data,organizationId})});e.target.reset();$('#incident-dialog').close();toast(id?'Einsatz aktualisiert':'Einsatz eröffnet');await loadAll();}catch(error){toast(error.message,true);}};
$('#resource-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const id=data.id;delete data.id;try{if(id){const current=state.resources.find(r=>r.id===id);await api(`/api/v1/resources/${id}`,{method:'PUT',body:JSON.stringify({...data,status:current.status})});}else await api('/api/v1/resources',{method:'POST',body:JSON.stringify({...data,organizationId})});e.target.reset();$('#resource-dialog').close();toast(id?'Einsatzmittel aktualisiert':'Einsatzmittel angelegt');await loadAll();}catch(error){toast(error.message,true);}};
$('#case-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const incidentId=data.incidentId;delete data.incidentId;try{await api(`/api/v1/cases/from-incident/${incidentId}`,{method:'POST',body:JSON.stringify(data)});$('#case-dialog').close();toast('Fallakte angelegt');await loadAll();showView('cases');const c=state.cases.find(x=>x.incidentId===incidentId);if(c)selectCase(c.id);}catch(error){toast(error.message,true);}};
$('#person-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId;delete data.caseId;data.role=+data.role;data.dateOfBirth=data.dateOfBirth||null;try{await api(`/api/v1/cases/${caseId}/persons`,{method:'POST',body:JSON.stringify(data)});$('#person-dialog').close();toast('Person zur Fallakte übernommen');await loadAll();await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#evidence-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId;delete data.caseId;data.status=+data.status;try{await api(`/api/v1/cases/${caseId}/evidence`,{method:'POST',body:JSON.stringify(data)});$('#evidence-dialog').close();toast('Asservat angelegt');await loadAll();await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#document-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId;delete data.caseId;data.type=+data.type;try{await api(`/api/v1/cases/${caseId}/documents`,{method:'POST',body:JSON.stringify(data)});$('#document-dialog').close();toast('Schreiben erstellt');await loadAll();await selectCase(caseId);}catch(error){toast(error.message,true);}};
$('#dispatch-form').onsubmit=async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));const caseId=data.caseId,documentId=data.documentId;delete data.caseId;delete data.documentId;try{await api(`/api/v1/cases/${caseId}/documents/${documentId}/dispatches`,{method:'POST',body:JSON.stringify(data)});$('#dispatch-dialog').close();toast('Schreiben abverfügt');await selectCase(caseId);}catch(error){toast(error.message,true);}};
setInterval(()=>$('#clock').textContent=new Date().toLocaleTimeString('de-DE',{hour:'2-digit',minute:'2-digit',second:'2-digit'}),1000);
let addressTimer;
$('#incident-form').elements.location.addEventListener('input',e=>{clearTimeout(addressTimer);const query=e.target.value.trim();if(query.length<2)return;addressTimer=setTimeout(async()=>{try{const addresses=await api(`/api/v1/addresses/search?query=${encodeURIComponent(query)}&limit=40`);$('#address-suggestions').innerHTML=addresses.map(a=>`<option value="${escapeHtml(a.displayName)}"></option>`).join('');}catch{}},250);});
loadAll();
