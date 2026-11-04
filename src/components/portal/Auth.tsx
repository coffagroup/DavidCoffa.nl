"use client";
import { useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { api, errorMessage, type PortalUser } from "@/lib/portal/api";
import { Notice, Loading } from "./Primitives";
import { LanguageToggle } from "./LanguageToggle";
import { useLanguage } from "@/components/LanguageProvider";

export function Login({ onLogin, mode = "inloggen" }: { onLogin: (user: PortalUser) => void; mode?: string }) {
  const { language } = useLanguage();
  const tx = (nl: string, en: string) => language === "en" ? en : nl;
  const params = useSearchParams();
  const router = useRouter();
  const [email, setEmail] = useState(params.get("email") ?? "");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [recovery, setRecovery] = useState("");
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState("");
  const [success, setSuccess] = useState("");
  const [invitation, setInvitation] = useState<Record<string, unknown> | null>(null);
  const token = params.get("token") ?? "";
  const inviting = mode === "uitnodiging";
  const resetting = mode === "wachtwoord-reset";
  const forgot = mode === "wachtwoord-vergeten";
  useEffect(() => {
    if (!inviting || !token) return;
    api<Record<string, unknown>>(`auth/invitations/${encodeURIComponent(token)}`).then(data => { setInvitation(data); if (data.email) setEmail(String(data.email)); }).catch(error => setProblem(errorMessage(error)));
  }, [inviting, token]);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setProblem(""); setSuccess("");
    try {
      if (inviting) { await api("auth/accept-invitation", {method:"POST",body:{token,name,password}}); setSuccess("Je account is geactiveerd. Je kunt nu inloggen."); }
      else if (forgot) { await api("auth/forgot-password", {method:"POST",body:{email}}); setSuccess("Je verzoek is verwerkt. Controleer je e-mail voor een eventuele resetlink. Ontvang je niets, neem dan contact op met David."); }
      else if (resetting) { await api("auth/reset-password", {method:"POST",body:{email,token,newPassword:password}}); setSuccess("Je wachtwoord is gewijzigd. Log opnieuw in."); }
      else { const user = await api<PortalUser>("auth/login", {method:"POST",body:{email,password,twoFactorCode:code || undefined,recoveryCode:recovery || undefined}}); onLogin(user); router.replace("/portaal"); }
    } catch (error) { setProblem(errorMessage(error)); } finally { setBusy(false); }
  }
  return <main className="os-auth">
    <div className="os-auth-story"><LanguageToggle/><Link href="/" className="os-auth-logo">dc<span>.</span></Link><span className="os-eyebrow">COFFA OS / {tx("KLANTENPORTAAL","CUSTOMER PORTAL")}</span><h1>{tx("Alles voor","Everything for")}<br />{tx("ons werk.","our work.")}<br /><em>{tx("Op één plek.","In one place.")}</em></h1><p>{tx("Projecten volgen, documenten terugvinden en samen verder met jouw vraag.","Track projects, find documents and work together on your goals.")}</p><Link href="/" className="os-auth-back">← {tx("Terug naar DavidCoffa.nl","Back to DavidCoffa.nl")}</Link></div>
    <div className="os-auth-main"><div className="os-auth-card"><span className="os-eyebrow">{tx("RECHTSTREEKS VERBONDEN","DIRECTLY CONNECTED")}</span><h2>{inviting?tx("Welkom bij Coffa OS.","Welcome to Coffa OS."):resetting?tx("Nieuw wachtwoord.","New password."):forgot?tx("Wachtwoord vergeten?","Forgot password?"):tx("Fijn dat je er bent.","Good to see you.")}</h2><p>{inviting?tx("Stel je account in via je persoonlijke uitnodiging.","Set up your account using your personal invitation."):forgot?tx("Vul je e-mailadres in om een resetlink aan te vragen.","Enter your email address to request a reset link."):tx("Log in om je eigen omgeving te openen.","Log in to access your workspace.")}</p><Notice error>{problem}</Notice><Notice>{success}</Notice>
    {inviting&&!token?<Notice error>{tx("Deze uitnodigingslink is onvolledig.","This invitation link is incomplete.")}</Notice>:<form className="os-form" onSubmit={submit}>
      {inviting&&!invitation&&!problem?<Loading/>:null}
      {inviting&&<label>{tx("Je naam","Your name")}<input required autoComplete="name" value={name} onChange={event=>setName(event.target.value)}/></label>}
      {!inviting&&<label>{tx("E-mailadres","Email address")}<input type="email" required autoComplete="username" value={email} onChange={event=>setEmail(event.target.value)}/></label>}
      {inviting&&invitation&&<p className="os-auth-invite">{tx("Uitnodiging voor","Invitation for")} <strong>{String(invitation.email??email)}</strong></p>}
      {!forgot&&<label>{inviting||resetting?tx("Kies een wachtwoord (minimaal 12 tekens)","Choose a password (at least 12 characters)"):tx("Wachtwoord","Password")}<input type="password" required minLength={inviting||resetting?12:undefined} autoComplete={inviting||resetting?"new-password":"current-password"} value={password} onChange={event=>setPassword(event.target.value)}/></label>}
      {!forgot&&!inviting&&!resetting&&<details className="os-auth-more"><summary>{tx("Authenticator of herstelcode gebruiken","Use an authenticator or recovery code")}</summary><label>{tx("Authenticatiecode","Authentication code")}<input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={event=>setCode(event.target.value)}/></label><label>{tx("Of een herstelcode","Or a recovery code")}<input autoComplete="off" value={recovery} onChange={event=>setRecovery(event.target.value)}/></label></details>}
      <button className="os-button primary" disabled={busy||(inviting&&!invitation)}>{busy?tx("Even geduld…","Please wait…"):inviting?tx("Account activeren","Activate account"):forgot?tx("Vraag resetlink aan","Request reset link"):resetting?tx("Wachtwoord wijzigen","Change password"):tx("Inloggen","Log in")} <span aria-hidden="true">↗</span></button>
    </form>}
    <div className="os-auth-links">{mode==="inloggen"?<Link href="/portaal/wachtwoord-vergeten">{tx("Wachtwoord vergeten?","Forgot password?")}</Link>:<Link href="/portaal/inloggen">{tx("Naar inloggen","Back to login")}</Link>}<span>{tx("Toegang is uitsluitend op uitnodiging.","Access is by invitation only.")}</span></div></div></div>
  </main>;
}

export function TwoFactorSetup({ onReady }: { onReady: (user: PortalUser) => void }) {
  const { language } = useLanguage();
  const tx = (nl: string, en: string) => language === "en" ? en : nl;
  const [setup, setSetup] = useState<{sharedKey:string;authenticatorUri:string} | null>(null);
  const [code, setCode] = useState(""); const [problem, setProblem] = useState(""); const [codes,setCodes] = useState<string[]>([]); const [user,setUser] = useState<PortalUser|null>(null); const [busy,setBusy]=useState(false);
  useEffect(() => { api<{sharedKey:string;authenticatorUri:string}>("auth/2fa/setup").then(setSetup).catch(error => setProblem(errorMessage(error))); }, []);
  async function enable(event:FormEvent) { event.preventDefault(); setBusy(true); setProblem(""); try { const result=await api<{recoveryCodes:string[];user:PortalUser}>("auth/2fa/enable",{method:"POST",body:{code}}); setCodes(result.recoveryCodes); setUser(result.user); } catch(error){setProblem(errorMessage(error));}finally{setBusy(false);} }
  return <main className="os-security-gate"><div className="os-security-card"><LanguageToggle/><span className="os-eyebrow">COFFA OS / {tx("ACCOUNTBEVEILIGING","ACCOUNT SECURITY")}</span><h1>{tx("Een extra sleutel","An extra key")}<br /><em>{tx("voor jouw omgeving.","for your workspace.")}</em></h1><p>{tx("Voor de beheeromgeving is een authenticator verplicht. Voeg deze sleutel toe aan je authenticator-app en vul de actuele code in.","An authenticator is required for the management workspace. Add this key to your authenticator app and enter the current code.")}</p><Notice error>{problem}</Notice>{codes.length?<><h2>{tx("Bewaar je herstelcodes","Save your recovery codes")}</h2><p>{tx("Bewaar deze codes op een veilige plek. Iedere code werkt één keer.","Store these codes somewhere safe. Each code can only be used once.")}</p><div className="os-recovery-codes">{codes.map(item=><code key={item}>{item}</code>)}</div><button className="os-button primary" onClick={()=>user&&onReady(user)}>{tx("Ik heb de codes veilig bewaard","I have saved the codes safely")} ↗</button></>:setup?<form className="os-form" onSubmit={enable}><label>{tx("Instelsleutel","Setup key")}<code className="os-secret">{setup.sharedKey}</code></label><a className="os-button secondary" href={setup.authenticatorUri}>{tx("Open in authenticator","Open in authenticator")}</a><label>{tx("Code uit je authenticator","Authenticator code")}<input required inputMode="numeric" autoComplete="one-time-code" value={code} onChange={event=>setCode(event.target.value)}/></label><button className="os-button primary" disabled={busy}>{busy?tx("Controleren…","Checking…"):tx("Beveiliging activeren","Enable security")} ↗</button></form>:<Loading/>}</div></main>;
}
