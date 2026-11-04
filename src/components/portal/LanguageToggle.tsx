"use client";

import { useLanguage } from "@/components/LanguageProvider";

export function LanguageToggle() {
  const { language, setLanguage } = useLanguage();
  const nextLanguage = language === "nl" ? "en" : "nl";

  return (
    <button
      type="button"
      className="os-text-button"
      aria-label={language === "nl" ? "Switch language to English" : "Verander de taal naar Nederlands"}
      aria-pressed={language === "en"}
      onClick={() => setLanguage(nextLanguage)}
    >
      {language === "nl" ? "EN" : "NL"}
    </button>
  );
}
