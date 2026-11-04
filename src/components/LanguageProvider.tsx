"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { usePathname } from "next/navigation";
import { englishCopy } from "@/lib/english-copy";

export type Language = "nl" | "en";
export type LanguageContextValue = {
  language: Language;
  setLanguage: (language: Language) => void;
  t: (nl: string, en: string) => string;
};

const LanguageContext = createContext<LanguageContextValue | null>(null);
const storageKey = "davidcoffa-language";
const textOriginals = new WeakMap<Text, string>();
const attributeOriginals = new WeakMap<Element, Map<string, string>>();
const translatedAttributes = ["aria-label", "title", "placeholder", "alt"];
const englishEntries = Object.entries(englishCopy)
  .filter(([nl]) => nl.length >= 5 || nl.includes(" "))
  .sort(([a], [b]) => b.length - a.length);
const englishPattern = new RegExp(englishEntries.map(([nl]) => nl.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|"), "g");

function getEnglish(value: string) {
  const trimmed = value.trim();
  const exact = englishCopy[trimmed];
  if (exact) {
    const start = value.indexOf(trimmed);
    return `${value.slice(0, start)}${exact}${value.slice(start + trimmed.length)}`;
  }
  return value.replace(englishPattern, nl => englishCopy[nl] ?? nl);
}

function isPublicNode(node: Node) {
  const element = node instanceof Element ? node : node.parentElement;
  return !element?.closest("input, textarea, select, [contenteditable='true'], [data-no-translate]");
}

function isTranslatableAttribute(element: Element) {
  return !element.closest("[contenteditable='true'], [data-no-translate]");
}

function translateTree(root: Node, language: Language) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let node: Text | null;
  while ((node = walker.nextNode() as Text | null)) {
    if (!isPublicNode(node)) continue;
    if (!textOriginals.has(node)) textOriginals.set(node, node.data);
    const original = textOriginals.get(node)!;
    const translated = language === "en" ? getEnglish(original) : original;
    if (node.data !== translated) node.data = translated;
  }

  const elements = root instanceof Element ? [root, ...root.querySelectorAll("*")] : [];
  for (const element of elements) {
    if (!isTranslatableAttribute(element)) continue;
    let originals = attributeOriginals.get(element);
    if (!originals) {
      originals = new Map();
      attributeOriginals.set(element, originals);
    }
    for (const name of translatedAttributes) {
      if (!element.hasAttribute(name)) continue;
      if (!originals.has(name)) originals.set(name, element.getAttribute(name) ?? "");
      const original = originals.get(name)!;
      const translated = language === "en" ? getEnglish(original) : original;
      if (element.getAttribute(name) !== translated) element.setAttribute(name, translated);
    }
  }
}

export function LanguageProvider({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const [language, updateLanguage] = useState<Language>("nl");

  const setLanguage = useCallback((next: Language) => {
    updateLanguage(next);
    try {
      window.localStorage.setItem(storageKey, next);
    } catch {
      // The preference remains available for the current page if storage is disabled.
    }
  }, []);

  useEffect(() => {
    try {
      const saved = window.localStorage.getItem(storageKey);
      if (saved === "nl" || saved === "en") updateLanguage(saved);
    } catch {
      // Keep Dutch as the default when storage is unavailable.
    }
  }, []);

  useEffect(() => {
    document.documentElement.lang = language;
    if (pathname === "/portaal" || pathname.startsWith("/portaal/")) return;
    const translate = () => translateTree(document.body, language);
    translate();
    const observer = new MutationObserver(() => translate());
    observer.observe(document.body, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: translatedAttributes });
    return () => observer.disconnect();
  }, [language, pathname]);

  const t = useCallback((nl: string, en: string) => language === "en" ? en : nl, [language]);
  const value = useMemo(() => ({ language, setLanguage, t }), [language, setLanguage, t]);
  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage(): LanguageContextValue {
  const value = useContext(LanguageContext);
  if (!value) throw new Error("useLanguage must be used within a LanguageProvider");
  return value;
}
