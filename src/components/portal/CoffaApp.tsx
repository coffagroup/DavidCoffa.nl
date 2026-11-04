"use client";
import Link from "next/link";
import { usePathname,useRouter,useSearchParams } from "next/navigation";
import { useEffect,useRef,useState,type FormEvent } from "react";
import { api,errorMessage,PortalError,type PortalUser } from "@/lib/portal/api";
import { can,modules } from "@/lib/portal/modules";
import { Login,TwoFactorSetup } from "./Auth";
import { Loading,Notice } from "./Primitives";
import { Records } from "./Records";
import { Detail,ActivityList } from "./Details";
import { Overview } from "./Overview";
import { Account,Users,Settings,Notifications } from "./Account";
import { VoiceAssistant } from "./VoiceAssistant";
import { BlogManager } from "./BlogManager";
import { WebsiteAnalytics } from "./WebsiteAnalytics";
import { TimeTools } from "./WorkTools";
import { LanguageToggle } from "./LanguageToggle";
import { useLanguage } from "@/components/LanguageProvider";
const groups=[{title:"RELATIES",symbol:"◫",keys:["klanten","contactpersonen","leads"]},{title:"COMMERCIEEL",symbol:"↗",keys:["offertes","facturen","abonnementen"]},{title:"FINANCIËN",symbol:"⌁",keys:["financien","kosten"]},{title:"WERKZAAMHEDEN",symbol:"☷",keys:["projecten","taken","uren","tickets"]},{title:"ORGANISATIE",symbol:"⚙",keys:["documenten","gebruikers","activiteiten","instellingen"]},{title:"MARKETING",symbol:"⌕",keys:["statistieken","blog"]}];
const titles:Record<string,string>={financien:"Financiën",gebruikers:"Gebruikers & uitnodigingen",activiteiten:"Activiteiten",instellingen:"Instellingen",blog:"SEO-blogstudio",statistieken:"Website-statistieken",account:"Mijn account",meldingen:"Meldingen"};
const icons:Record<string,string>={klanten:"◫",contactpersonen:"♧",leads:"↗",offertes:"▤",facturen:"▥",abonnementen:"↻",financien:"⌁",kosten:"−",projecten:"□",taken:"☷",uren:"◷",tickets:"◉",documenten:"▱",gebruikers:"♧",activiteiten:"⌁",instellingen:"⚙",blog:"✎",statistieken:"⌕"};
export default function CoffaApp(){
 const{language}=useLanguage();
 const[user,setUser]=useState<PortalUser|null>(null);const[loading,setLoading]=useState(true);const[problem,setProblem]=useState("");const[menu,setMenu]=useState(false);const[assistant,setAssistant]=useState(false);const[search,setSearch]=useState("");const[revision,setRevision]=useState(0);const menuRef=useRef<HTMLButtonElement>(null);
 const pathname=usePathname();const router=useRouter();const params=useSearchParams();const parts=pathname.split("/").filter(Boolean).slice(1);const section=parts[0]??"";const id=parts[1];
 const[sidebarCollapsed,setSidebarCollapsed]=useState(false);const[desktopSidebar,setDesktopSidebar]=useState(false);
 const[openGroups,setOpenGroups]=useState<string[]>(()=>["SAMENWERKEN",...groups.filter(group=>group.keys.includes(section)).map(group=>group.title)]);
 const railCollapsed=sidebarCollapsed&&desktopSidebar;
 useEffect(()=>{
   const media=window.matchMedia("(min-width: 1001px)");const update=()=>setDesktopSidebar(media.matches);update();media.addEventListener("change",update);
   try{setSidebarCollapsed(localStorage.getItem("coffa.sidebar.collapsed")==="true");const saved=JSON.parse(localStorage.getItem("coffa.sidebar.groups")??"null");if(Array.isArray(saved)&&saved.every(item=>typeof item==="string"))setOpenGroups([...new Set([...saved,...groups.filter(group=>group.keys.includes(section)).map(group=>group.title)])]);}catch{}
   return()=>media.removeEventListener("change",update);
   // Read layout preferences once; active sections are expanded below on navigation.
   // eslint-disable-next-line react-hooks/exhaustive-deps
 },[]);
 useEffect(()=>{const active=groups.find(group=>group.keys.includes(section));if(active)setOpenGroups(previous=>previous.includes(active.title)?previous:[...previous,active.title]);},[section]);
 function collapseSidebar(next:boolean){setSidebarCollapsed(next);try{localStorage.setItem("coffa.sidebar.collapsed",String(next));}catch{}}
 function toggleGroup(title:string){
   if(railCollapsed)collapseSidebar(false);
   setOpenGroups(previous=>{const next=railCollapsed?[...new Set([...previous,title])]:previous.includes(title)?previous.filter(item=>item!==title):[...previous,title];try{localStorage.setItem("coffa.sidebar.groups",JSON.stringify(next));}catch{}return next;});
 }

 useEffect(()=>{let active=true;api<PortalUser>("auth/me").then(result=>{if(active)setUser(result);}).catch(error=>{if(active&&!(error instanceof PortalError&&error.status===401))setProblem(errorMessage(error));}).finally(()=>{if(active)setLoading(false);});return()=>{active=false;};},[]);
 useEffect(()=>setMenu(false),[pathname]);
 useEffect(()=>{function escape(event:KeyboardEvent){if(event.key==="Escape"&&menu){setMenu(false);menuRef.current?.focus();}}document.addEventListener("keydown",escape);return()=>document.removeEventListener("keydown",escape);},[menu]);
 async function logout(){try{await api("auth/logout",{method:"POST",body:{}});setUser(null);router.replace("/portaal/inloggen");}catch(error){setProblem(errorMessage(error));}}
 function allowed(key:string){if(!user)return false;if(key==="account"||key==="inloggen"||!key)return true;if(key==="blog"||key==="statistieken"||key==="gebruikers")return user.role==="SuperAdmin";if(key==="meldingen")return user.role==="Customer"||can(user,"tickets");if(key==="instellingen")return user.role==="SuperAdmin"||user.permissions.includes("settings.manage");if(key==="activiteiten")return user.role!=="Customer";if(key==="financien")return can(user,"finance");const module=modules.find(item=>item.key===key);return module?user.role==="Customer"?Boolean(module.customer):can(user,module.permission):false;}
 const authModes:Record<string,string>={inloggen:"inloggen",uitnodiging:"uitnodiging",reset:"wachtwoord-reset","wachtwoord-reset":"wachtwoord-reset","wachtwoord-vergeten":"wachtwoord-vergeten"};
 if(section==="verifieer")return <VerifyEmail email={params.get("email")??""} token={params.get("token")??""}/>;
 if(loading)return <div className="os-initial"><span className="os-logo">dc<span>.</span></span><Loading/></div>;
 if(authModes[section]&&section!=="inloggen")return <Login mode={authModes[section]} onLogin={setUser}/>;
 if(!user)return <><Notice error>{problem}</Notice><Login onLogin={next=>{setUser(next);setProblem("");}} mode={authModes[section]??"inloggen"}/></>;
 if(user.requiresTwoFactorSetup)return <TwoFactorSetup onReady={setUser}/>;
 const sourceModule=modules.find(item=>item.key===section);const customer=user.role==="Customer";
 const target=allowed("klanten")?"klanten":allowed("tickets")?"tickets":"projecten";
 function searchSubmit(event:FormEvent){event.preventDefault();router.push(`/portaal/${target}?q=${encodeURIComponent(search)}`);}
 const englishTitles:Record<string,string>={klanten:"Clients",contactpersonen:"Contacts",leads:"Sales pipeline",offertes:"Quotes",facturen:"Invoices",abonnementen:"Subscriptions",financien:"Finances",kosten:"Business expenses",projecten:"Projects",taken:"Tasks",uren:"Time tracking",tickets:"Support tickets",documenten:"Documents",gebruikers:"Users & invitations",activiteiten:"Activity",instellingen:"Settings",blog:"SEO blog studio",statistieken:"Website analytics",account:"My account",meldingen:"Notifications"};
 const tx=(nl:string,en:string)=>language==="en"?en:nl;
 const englishSingular:Record<string,string>={klanten:"Client",contactpersonen:"Contact",leads:"Lead",offertes:"Quote",facturen:"Invoice",abonnementen:"Subscription",kosten:"Expense",projecten:"Project",taken:"Task",uren:"Time entry",tickets:"Support ticket",documenten:"Document"};
 const englishDescriptions:Record<string,string>={klanten:"One file for each client and all related work.",contactpersonen:"The people behind your client organizations.",leads:"Track each opportunity from first contact to new client.",offertes:"Turn a clear proposal into an agreement and next steps.",facturen:"Invoices, partial payments and outstanding balances.",kosten:"Record business expenses with dates, suppliers and VAT.",abonnementen:"Hosting, maintenance and software contracts with recurring invoices.",projecten:"Progress, agreements and work in motion.",taken:"See what needs attention now.",uren:"Track time against the client and work it was spent on.",tickets:"Questions, replies and a clear next step.",documenten:"Files shared carefully with the right people."};
 const module=sourceModule&&language==="en"?{...sourceModule,title:englishTitles[sourceModule.key]??sourceModule.title,singular:englishSingular[sourceModule.key]??sourceModule.singular,description:englishDescriptions[sourceModule.key]??sourceModule.description}:sourceModule;
 const title=language==="en"?englishTitles[section]??"Overview":module?.title??titles[section]??"Overzicht";
 const displayKey=(key:string)=>language==="en"?englishTitles[key]??key:modules.find(item=>item.key===key)?.title??titles[key]??key;
 const nav=(key:string)=><Link href={`/portaal/${key}`} key={key} onClick={()=>setMenu(false)} aria-current={section===key?"page":undefined}><span aria-hidden="true">{icons[key]??"·"}</span><span className="os-nav-label">{customer&&key==="projecten"?tx("Mijn projecten","My projects"):displayKey(key)}</span></Link>;
 function navigationGroup(title:string,symbol:string,keys:string[]){
   if(!keys.length)return null;const expanded=!railCollapsed&&openGroups.includes(title);const panelId=`os-nav-${title.toLowerCase()}`;
   const groupLabel=language==="en"?({RELATIES:"RELATIONSHIPS",COMMERCIEEL:"COMMERCIAL",FINANCIËN:"FINANCE",WERKZAAMHEDEN:"WORK",ORGANISATIE:"ORGANIZATION",SAMENWERKEN:"COLLABORATION"} as Record<string,string>)[title]??title:title;
   return <div className="os-nav-group" key={title}><button type="button" className="os-nav-group-toggle" aria-expanded={expanded} aria-controls={panelId} title={railCollapsed?groupLabel:undefined} aria-label={railCollapsed?`${groupLabel.toLowerCase()} open`:undefined} onClick={()=>toggleGroup(title)}><span className="os-group-symbol" aria-hidden="true">{symbol}</span><span className="os-group-label">{groupLabel}</span><svg className="os-group-chevron" aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="m9 5 7 7-7 7"/></svg></button><div className="os-nav-group-items" id={panelId} hidden={!expanded}>{keys.map(nav)}</div></div>;
 }
 return <div className={`os-app${menu?" is-menu-open":""}${railCollapsed?" is-sidebar-collapsed":""}`}>
  <a href="#os-main" className="skip-link">{tx("Naar inhoud","Skip to content")}</a>
  {menu&&<button className="os-sidebar-scrim" aria-label={tx("Menu sluiten","Close menu")} onClick={()=>setMenu(false)}/>}
  <aside className="os-sidebar">
   <div className="os-sidebar-header">
    <Link href="/portaal" className="os-brand" aria-label="Coffa OS dashboard"><span className="os-logo">dc<span>.</span></span><span className="os-brand-name">Coffa OS<small>{customer?tx("KLANTENPORTAAL","CUSTOMER PORTAL"):tx("BEDRIJFSOMGEVING","BUSINESS WORKSPACE")}</small></span></Link>
    <button type="button" className="os-sidebar-collapse" aria-label={railCollapsed?tx("Zijbalk uitklappen","Expand sidebar"):tx("Zijbalk inklappen","Collapse sidebar")} title={railCollapsed?tx("Zijbalk uitklappen","Expand sidebar"):tx("Zijbalk inklappen","Collapse sidebar")} aria-expanded={!railCollapsed} aria-controls="os-sidebar-navigation" onClick={()=>collapseSidebar(!sidebarCollapsed)}><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="m14 6-6 6 6 6"/></svg></button>
   </div>
   <nav id="os-sidebar-navigation" aria-label={tx("Portaalnavigatie","Portal navigation")}>
    <Link href="/portaal" aria-current={!section?"page":undefined} title={railCollapsed?customer?tx("Mijn overzicht","My overview"):"Dashboard":undefined} aria-label={customer?tx("Mijn overzicht","My overview"):"Dashboard"}><span aria-hidden="true">▦</span><span className="os-nav-label">{customer?tx("Mijn overzicht","My overview"):"Dashboard"}</span></Link>
    {customer?navigationGroup("SAMENWERKEN","◫",["projecten","tickets","documenten","facturen","offertes"]):groups.map(group=>navigationGroup(group.title,group.symbol,group.keys.filter(allowed)))}
   </nav>
   <div className="os-sidebar-bottom">
    <Link href="/portaal/account" title={railCollapsed?tx("Mijn account","My account"):undefined} aria-label={tx("Mijn account","My account")}><span className="os-avatar">{user.name.slice(0,1).toUpperCase()}</span><span className="os-sidebar-person">{user.name}<small>{customer?tx("Klant","Customer"):user.role==="SuperAdmin"?"Founder":tx("Medewerker","Employee")}</small></span></Link>
    <Link href="/" title={railCollapsed?tx("Naar de website","Go to website"):undefined} aria-label={tx("Naar de website","Go to website")}><span aria-hidden="true">←</span><span className="os-nav-label">{tx("Naar de website","Go to website")}</span></Link>
   </div>
  </aside>
  <div className="os-workspace">
   <header className="os-topbar">
    <div><button ref={menuRef} className="os-menu-toggle" aria-label={menu?tx("Menu sluiten","Close menu"):tx("Menu openen","Open menu")} aria-expanded={menu} onClick={()=>setMenu(value=>!value)}>☰</button><span className="os-topbar-location">Coffa OS <span>/</span> <strong>{title}</strong></span></div>
    <form className="os-top-search" onSubmit={searchSubmit}><label><span className="sr-only">{tx("Zoek in","Search")} {displayKey(target).toLowerCase()}</span><input type="search" placeholder={`${tx("Zoek in","Search")} ${displayKey(target).toLowerCase()}…`} value={search} onChange={event=>setSearch(event.target.value)}/></label><button aria-label={tx("Zoeken","Search")}>⌕</button></form>
    <div className="os-top-actions">{user.role==="SuperAdmin"&&<button type="button" className="os-button secondary os-assistant-launch" onClick={()=>setAssistant(true)}>{tx("Inspreken","Voice input")}</button>}{allowed("meldingen")&&<Link href="/portaal/meldingen" className="os-icon-button" aria-label={tx("Meldingen","Notifications")}>♧</Link>}<LanguageToggle/><Link className="os-avatar" href="/portaal/account" aria-label={tx("Mijn account","My account")}>{user.name.slice(0,1).toUpperCase()}</Link><button className="os-text-button" onClick={logout}>{tx("Uitloggen","Log out")}</button></div>
   </header>
   <main id="os-main" className="os-main"><Notice error>{problem}</Notice>{!allowed(section)?<div className="os-panel"><h1>{tx("Geen toegang tot dit onderdeel.","You don't have access to this section.")}</h1><p>{tx("Je account heeft hiervoor geen rechten. Neem contact op met David als je toegang nodig hebt.","Your account doesn't have permission to access this section. Contact David if you need access.")}</p><Link className="os-button secondary" href="/portaal">{tx("Naar mijn overzicht","Go to my overview")}</Link></div>:!section||section==="inloggen"?<Overview user={user}/>:section==="financien"?<Overview user={user} financePage/>:section==="statistieken"?<WebsiteAnalytics/>:section==="blog"?<BlogManager/>:section==="account"?<Account user={user} onUser={setUser} onLogout={()=>{setUser(null);router.replace("/portaal/inloggen");}}/>:section==="gebruikers"?<Users user={user}/>:section==="instellingen"?<Settings user={user}/>:section==="activiteiten"?<ActivityList/>:section==="meldingen"?<Notifications/>:module?id?<Detail module={module} id={id} user={user}/>:<Records key={`${module.key}-${revision}`} module={module} user={user} extraActions={section==="uren"&&can(user,"time",true)?<TimeTools user={user} onChanged={()=>setRevision(value=>value+1)}/>:undefined}/>:null}</main>
   <footer className="os-app-footer"><span>Coffa OS · David Coffa</span><span>{customer?tx("Samen verder, op één plek.","Moving forward together."):tx("Gebouwd voor de dagelijkse praktijk.","Built for everyday work.")}</span></footer>
  </div>
  {assistant&&user.role==="SuperAdmin"&&<VoiceAssistant user={user} onClose={()=>setAssistant(false)}/>}
 </div>;
}
function VerifyEmail({email,token}:{email:string;token:string}){const{language}=useLanguage();const tx=(nl:string,en:string)=>language==="en"?en:nl;const[problem,setProblem]=useState("");const[done,setDone]=useState(false);const[busy,setBusy]=useState(false);return <main className="os-security-gate"><div className="os-security-card"><LanguageToggle/><span className="os-eyebrow">COFFA OS / {tx("ACCOUNT","ACCOUNT")}</span><h1>{tx("E-mailadres verifiëren.","Verify email address.")}</h1><Notice error>{problem}</Notice>{done?<><p>{tx("Je e-mailadres is bevestigd.","Your email address has been verified.")}</p><Link className="os-button primary" href="/portaal/inloggen">{tx("Naar inloggen","Back to login")} ↗</Link></>:<><p>{tx("Bevestig dat dit jouw persoonlijke verificatielink is.","Confirm that this is your personal verification link.")}</p><button className="os-button primary" disabled={busy||!email||!token} onClick={async()=>{setBusy(true);try{await api("auth/verification/confirm",{method:"POST",body:{email,token}});setDone(true);}catch(error){setProblem(errorMessage(error));}finally{setBusy(false);}}}>{busy?tx("Controleren…","Checking…"):tx("E-mailadres bevestigen","Verify email address")}</button></>}</div></main>;}
