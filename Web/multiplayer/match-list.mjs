// Shared by the server-generated browser script and focused state tests.
// Presentation follows explicit match seats, never player names or UI copy.
export function matchState(match) {
  if (match.status === "waiting") return "lobbies";
  if (match.status !== "active") return "finished";
  return Number.isInteger(match.seatIndex) && match.seatIndex >= 0 &&
    match.seatIndex === match.currentTurnSeatIndex ? "your-turn" : "waiting";
}

export const matchListScript = `${matchState.toString()}
const matchSections=[['your-turn','Your turn'],['waiting','Waiting'],['lobbies','Lobbies'],['finished','Finished matches']];
let openMatchId=null,returnMatchFocus=null;
function matchOpponents(match){return match.players.filter(player=>player.seat!==match.seatIndex)}
function matchTitle(match){const opponents=matchOpponents(match);return opponents.length?'vs '+opponents.map(player=>player.name).join(', '):match.kind==='public'?'Online lobby':'Your private lobby'}
function matchStatus(match){
  const state=matchState(match),joined=match.players.length,count=match.seatCount??2;
  if(state==='lobbies')return match.seatIndex!==0?'Waiting for host to start':joined>=2?'Ready to start':'Waiting for players';
  if(state==='finished')return match.winnerSeatIndex===match.seatIndex?'You won':'Match finished';
  if(state==='your-turn')return 'Your turn';
  const player=match.players.find(player=>player.seat===match.currentTurnSeatIndex);
  return count>2?'Waiting for '+(player?.name||'opponent'):'Opponent’s turn';
}
function renderMatches(){
  const container=$('matches');
  for(const [state,title] of matchSections){
    const matches=lobby.matches.filter(match=>matchState(match)===state);
    if(!matches.length)continue;
    const section=node(state==='finished'?'details':'section',undefined,'match-group '+state);
    const heading=node(state==='finished'?'summary':'h2',undefined,'match-group-heading');
    heading.append(node('span',title),node('span',String(matches.length),'group-count'));
    if(state!=='finished'){heading.id='heading-'+state;section.setAttribute('aria-labelledby',heading.id)}
    if(state==='finished')section.open=finishedMatchesOpen;
    section.addEventListener('toggle',()=>{if(state==='finished')finishedMatchesOpen=section.open});
    section.append(heading);
    const list=node('div',undefined,'match-list');section.append(list);container.append(section);
    for(const match of matches){
      const row=node('article',undefined,'match-row '+state);row.id='match-'+match.id;
      row.tabIndex=-1;if(match.id===selectedMatch)row.classList.add('selected');
      const copy=node('div',undefined,'match-copy');row.append(copy);
      copy.append(node('h3',matchTitle(match),'match-title'));
      copy.append(node('p',state==='lobbies'?match.players.length+'/'+(match.seatCount??2)+' joined':'Round '+match.turnNumber+' · '+(match.seatCount??2)+' players','match-meta'));
      copy.append(node('p',matchStatus(match),'match-status'));
      const actions=node('div',undefined,'match-actions');row.append(actions);
      if(state==='lobbies'){
        if(match.seatIndex===0){
          if(match.players.length>=2)button(actions,'Start',()=>startMatch(match),false);
          else button(actions,'Invite',()=>inviteToLobby(match));
        }else button(actions,'Details',()=>openMatchDetails(match.id));
      }else{
        const link=node('a',state==='your-turn'?'Play':'View','button '+(state==='your-turn'?'':'secondary'));
        link.href='/?match='+match.id;link.dataset.focusKey='play-'+match.id;
        link.setAttribute('aria-label',(state==='your-turn'?'Play ':'View ')+matchTitle(match));actions.append(link);
      }
      actions.querySelectorAll('button').forEach((control,index)=>control.dataset.focusKey='row-action-'+match.id+'-'+index);
      const more=node('button','⋯','match-more');more.type='button';more.disabled=busy;
      more.dataset.focusKey='details-'+match.id;more.setAttribute('aria-label','Details for '+matchTitle(match));
      more.addEventListener('click',()=>openMatchDetails(match.id));actions.append(more);
      list.append(row);
    }
  }
  if(!lobby.matches.length)empty(container,'No matches yet. Choose New game to find a match or invite friends.');
}
let finishedMatchesOpen=false;
async function startMatch(match){const result=await api('matches/'+match.id+'/start',{});location.href='/?match='+result.match.id}
function inviteToLobby(match){selectedMatch=match.id;$('match-dialog').close();selectTab('friends');$('notice').textContent='Choose a friend or search for a player, then invite them to this lobby.'}
function openMatchDetails(id){
  const match=lobby.matches.find(item=>item.id===id);if(!match)return;
  openMatchId=id;returnMatchFocus='details-'+id;renderMatchDetails(match);$('match-dialog').showModal();
}
function renderMatchDetails(match){
  $('match-title').textContent=matchTitle(match);
  $('match-description').textContent='Standard 7×7 · '+(match.seatCount??2)+' players · '+matchStatus(match);
  const seats=$('match-seats');seats.replaceChildren();
  for(let seat=0;seat<(match.seatCount??2);seat++){
    const player=match.players.find(player=>player.seat===seat);
    const item=node('li',['Blue','Red','Green','Yellow'][seat]+' · '+(player?player.name+(seat===match.seatIndex?' (You)':'')+(seat===0?' · Host':''):'Open seat'),player?'occupied':'vacant');
    item.dataset.colour=['blue','red','green','yellow'][seat];seats.append(item);
  }
  const actions=$('match-detail-actions');actions.replaceChildren();
  if(match.status==='waiting'){
    if(match.seatIndex===0){
      if(match.players.length>=2)button(actions,'Start game with '+match.players.length+' players',()=>startMatch(match),false);
      else {const start=node('button','Start game');start.type='button';start.disabled=true;start.dataset.waiting='true';actions.append(start)}
      if(match.players.length<(match.seatCount??2))button(actions,'Invite friends',()=>inviteToLobby(match));
      button(actions,'Cancel lobby',async()=>{await api('matches/'+match.id+'/cancel',{});$('match-dialog').close();if(selectedMatch===match.id){selectedMatch=null;history.replaceState(null,'','/multiplayer')}$('notice').textContent='Waiting lobby cancelled.';await refresh()});
    }else actions.append(node('p','Only the host can start this game','detail-note'));
  }else play(actions,match,matchState(match)==='your-turn'?'Play':'View match');
  actions.querySelectorAll('button,a').forEach((control,index)=>control.dataset.focusKey='match-action-'+match.id+'-'+index);
  const social=$('match-players');social.replaceChildren();
  for(const player of matchOpponents(match)){
    const row=node('div',undefined,'opponent-actions');row.append(node('span',player.name,'opponent-name'));social.append(row);socialActions(row,player);
  }
  social.querySelectorAll('button,a').forEach((control,index)=>control.dataset.focusKey='match-social-'+match.id+'-'+index);
}
$('match-close').addEventListener('click',()=>$('match-dialog').close());
$('match-dialog').addEventListener('close',()=>{openMatchId=null;const control=document.querySelector('[data-focus-key="'+returnMatchFocus+'"]');if(control&&!$('social-dialog').open)control.focus({preventScroll:true})});
$('new-game').addEventListener('click',()=>{selectTab('matches');$('new-game-dialog').showModal()});
$('new-game-close').addEventListener('click',()=>$('new-game-dialog').close());
`;
