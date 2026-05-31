const $ = (selector, scope = document) => scope.querySelector(selector);
const $$ = (selector, scope = document) => Array.from(scope.querySelectorAll(selector));

const root = document.documentElement;
const nav = $(".nav");
const menuToggle = $(".menu-toggle");
const navLinks = $(".nav-links");
const themeToggle = $(".theme-toggle");
const yearElement = $("#year");
const contactForm = $("#contactForm");

if (yearElement) {
  yearElement.textContent = new Date().getFullYear();
}

function setTheme(theme) {
  root.dataset.theme = theme;
  localStorage.setItem("theme", theme);

  if (!themeToggle) return;

  const icon = $("i", themeToggle);
  if (!icon) return;

  icon.className = theme === "dark" ? "fa-solid fa-sun" : "fa-solid fa-moon";
}

function getPreferredTheme() {
  const storedTheme = localStorage.getItem("theme");

  if (storedTheme === "dark" || storedTheme === "light") {
    return storedTheme;
  }

  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

setTheme(getPreferredTheme());

themeToggle?.addEventListener("click", () => {
  const nextTheme = root.dataset.theme === "dark" ? "light" : "dark";
  setTheme(nextTheme);
});

function closeMenu() {
  if (!menuToggle || !navLinks) return;

  menuToggle.classList.remove("is-open");
  navLinks.classList.remove("is-open");
  document.body.classList.remove("menu-open");
  menuToggle.setAttribute("aria-expanded", "false");
  menuToggle.setAttribute("aria-label", "Menu openen");
}

function openMenu() {
  if (!menuToggle || !navLinks) return;

  menuToggle.classList.add("is-open");
  navLinks.classList.add("is-open");
  document.body.classList.add("menu-open");
  menuToggle.setAttribute("aria-expanded", "true");
  menuToggle.setAttribute("aria-label", "Menu sluiten");
}

menuToggle?.addEventListener("click", () => {
  const isOpen = menuToggle.classList.contains("is-open");
  isOpen ? closeMenu() : openMenu();
});

$$(".nav-links a").forEach((link) => {
  link.addEventListener("click", closeMenu);
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    closeMenu();
  }
});

document.addEventListener("click", (event) => {
  if (!navLinks || !menuToggle) return;

  const clickedInsideMenu = navLinks.contains(event.target);
  const clickedToggle = menuToggle.contains(event.target);

  if (!clickedInsideMenu && !clickedToggle) {
    closeMenu();
  }
});

function handleNavState() {
  if (!nav) return;

  if (window.scrollY > 20) {
    nav.classList.add("scrolled");
  } else {
    nav.classList.remove("scrolled");
  }
}

handleNavState();
window.addEventListener("scroll", handleNavState, { passive: true });

const revealItems = $$(".reveal");

if ("IntersectionObserver" in window) {
  const revealObserver = new IntersectionObserver(
    (entries, observer) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;

        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      });
    },
    {
      threshold: 0.16,
      rootMargin: "0px 0px -40px 0px"
    }
  );

  revealItems.forEach((item) => revealObserver.observe(item));
} else {
  revealItems.forEach((item) => item.classList.add("is-visible"));
}

const sections = $$("main section[id], header[id]");
const menuLinks = $$(".nav-links a[href^='#']");

if ("IntersectionObserver" in window && sections.length > 0) {
  const activeObserver = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;

        const id = entry.target.getAttribute("id");

        menuLinks.forEach((link) => {
          const isActive = link.getAttribute("href") === `#${id}`;
          link.classList.toggle("active", isActive);
        });
      });
    },
    {
      rootMargin: "-35% 0px -55% 0px",
      threshold: 0
    }
  );

  sections.forEach((section) => activeObserver.observe(section));
}

contactForm?.addEventListener("submit", (event) => {
  event.preventDefault();

  const formData = new FormData(contactForm);

  const name = String(formData.get("name") || "").trim();
  const email = String(formData.get("email") || "").trim();
  const subject = String(formData.get("subject") || "").trim();
  const message = String(formData.get("message") || "").trim();

  const mailSubject = encodeURIComponent(`Projectaanvraag via davidcoffa.nl - ${subject}`);

  const mailBody = encodeURIComponent(
    `Naam: ${name}
E-mail: ${email}
Onderwerp: ${subject}

Bericht:
${message}`
  );

  window.location.href = `mailto:info@davidcoffa.nl?subject=${mailSubject}&body=${mailBody}`;
});