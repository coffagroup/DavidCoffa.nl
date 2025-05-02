/* ----------------------------------------------------
   Helpers & globals
---------------------------------------------------- */
const $  = sel => document.querySelector(sel);
const $$ = sel => document.querySelectorAll(sel);

/* ----------------------------------------------------
   0. Pre‑loader (progress‑bar + confetti burst)
---------------------------------------------------- */
(function () {
  const progressBar = $('.progress-bar');
  let done = false;                         // voorkomt dubbele calls

  const doneEvents = ['animationend', 'transitionend'];
  if (progressBar) {
    doneEvents.forEach(evt =>
      progressBar.addEventListener(evt, hidePreloader, { once: true })
    );
  }

  // harde fallback: max 5 s na volledige paginalaad
  window.addEventListener('load', () => setTimeout(hidePreloader, 5000));

  async function hidePreloader () {
    if (done) return;
    done = true;

    const preloader = $('#preloader');
    if (!preloader) return;

    preloader.classList.add('hide');

    // element écht weghalen nadat de fade klaar is
    preloader.addEventListener('transitionend', () => preloader.remove(), {
      once: true
    });
    setTimeout(() => preloader.remove(), 400); // fallback

    // confetti 🎉
    const { default: confetti } =
      await import(
        'https://cdn.jsdelivr.net/npm/canvas-confetti@1.6.0/dist/confetti.browser.min.js'
      );

    const end = Date.now() + 3000;
    const cfg = {
      spread: 360,
      startVelocity: 50,
      ticks: 60,
      gravity: 0.5,
      decay: 0.94
    };

    (function shoot () {
      confetti({
        ...cfg,
        particleCount: 60,
        origin: { x: Math.random(), y: Math.random() * 0.4 }
      });
      if (Date.now() < end) setTimeout(shoot, 250);
    })();
  }
})();

/* ----------------------------------------------------
   1. Meertalige ondersteuning
---------------------------------------------------- */
const translations = {
  en: {
    // Nav
    'nav.services': 'Services',
    'nav.work': 'Work',
    'nav.about': 'About',
    'nav.contact': 'Contact',
    // Hero
    'hero.intro':
      'Hi, I\'m <span class="gradient-text">David&nbsp;Coffa</span> 👋',
    'hero.subtitle':
      'Web & UX developer who builds <strong>AI integrations</strong>, <strong>cloud platforms</strong> and <strong>data‑driven marketing</strong> to help your brand <span class="pop tooltip" data-tip="🚀 Go!">grow</span>.',
    'hero.cta': 'Let\'s talk',
    'hero.tooltip': '🚀 Go!',
    // Services
    'services.title': 'Services',
    'services.uxweb': 'UX & Web',
    'services.uxweb.desc':
      'Pixel‑perfect front‑end, scalable back‑end and high‑performance hosting.',
    'services.ai': 'AI Integrations',
    'services.ai.desc':
      'GPT‑powered chatbots, recommendation engines & process automation.',
    'services.cloud': 'Cloud Solutions',
    'services.cloud.desc':
      'Serverless architecture, CI/CD pipelines and IaC with Terraform.',
    'services.seo': 'SEO & SEA',
    'services.seo.desc':
      'Technical SEO, content strategy and profitable ad campaigns.',
    'services.branding': 'Branding',
    'services.branding.desc':
      'Brand storytelling, design systems and consistent visual identity.',
    'services.python': 'Python Dev',
    'services.python.desc':
      'Data pipelines, FastAPI services & AI prototypes in record time.',
    'services.react': 'React Apps',
    'services.react.desc':
      'PWAs & mobile‑first front‑ends with Next.js and blazing‑fast SSR.',
    'services.csharp': '.NET & C#',
    'services.csharp.desc':
      'Enterprise‑grade APIs, microservices & Azure DevOps pipelines.',
    // Work
    'work.title': 'Selected Work ✨',
    'work.card1.title': 'AI‑chatbot for 24/7 support',
    'work.card1.desc':
      'Conversational AI, GPT‑4o fine‑tuning & semantic search.',
    'work.card2.title': 'Serverless Cloud Platform',
    'work.card2.desc': 'Auto‑scaling microservices on AWS & Terraform IaC.',
    'work.card3.title': 'Headless Commerce',
    'work.card3.desc':
      'Nuxt storefront with Shopify Hydrogen and real‑time stock‑sync.',
    'work.card4.title': 'Process Automation Software',
    'work.card4.desc': 'Robotic Process Automation & workflow orchestration.',
    // About
    'about.title': 'About me',
    'about.text':
      'With a background in UX design and development I build scalable products that put <em>user experience</em> and <em>performance</em> first. I believe in <strong>design systems</strong>, <strong>test automation</strong> and <strong>open communication</strong>.',
    // Contact
    'contact.title': 'Contact',
    'contact.text':
      'New project or want to chat about AI? Email me at <a href="mailto:hello@davidcoffa.nl">hello@davidcoffa.nl</a> or schedule a call.',
    'contact.cta': 'Send an email',
    // Footer
    'footer.copy':
      '&copy; <span id="year"></span> David Coffa — Made with <span aria-label="coffee" role="img">☕️</span>'
  }
};

const translatableEls = [...$$('[data-i18n]')];
translatableEls.forEach(el => (el.dataset.nl = el.innerHTML.trim()));
$$('[data-i18n-tip]').forEach(
  el => (el.dataset.nlTip = el.getAttribute('data-tip'))
);

const langBtn = $('.lang-toggle');
const currentLang = localStorage.getItem('lang') || 'nl';
setLanguage(currentLang);

langBtn.addEventListener('click', () =>
  setLanguage(document.documentElement.dataset.lang === 'nl' ? 'en' : 'nl')
);

function setLanguage (lang) {
  document.documentElement.dataset.lang = lang;
  localStorage.setItem('lang', lang);
  if (langBtn) langBtn.textContent = lang === 'nl' ? 'EN' : 'NL';

  translatableEls.forEach(el => {
    const key = el.dataset.i18n;
    if (lang === 'nl') {
      el.innerHTML = el.dataset.nl;
    } else {
      el.innerHTML = translations[lang][key] || el.dataset.nl;
    }
  });

  $$('[data-i18n-tip]').forEach(el => {
    const key = el.dataset.i18nTip;
    if (lang === 'nl') {
      el.setAttribute('data-tip', el.dataset.nlTip);
    } else {
      el.setAttribute('data-tip', translations[lang][key] || el.dataset.nlTip);
    }
  });
}

/* ----------------------------------------------------
   2. Dynamisch jaar + mobile menu
---------------------------------------------------- */
$('#year').textContent = new Date().getFullYear();

function toggleMenu (btn) {
  $('.nav-links').classList.toggle('open');
  btn.classList.toggle('open');
}
$$('.nav-links a').forEach(link =>
  link.addEventListener('click', () => {
    $('.nav-links').classList.remove('open');
    $('.menu-toggle').classList.remove('open');
  })
);

/* ----------------------------------------------------
   3. Flying emoji‑achtergrond
---------------------------------------------------- */
const EMOJIS = [
  '😀',
  '🦄',
  '🚀',
  '🌟',
  '✨',
  '🎉',
  '🤖',
  '👾',
  '🎈',
  '💡',
  '🔥',
  '🥳',
  '😎',
  '🎨',
  '💻'
];
function spawnEmoji () {
  const el = document.createElement('span');
  el.className = 'emoji';
  el.textContent = EMOJIS[(Math.random() * EMOJIS.length) | 0];
  el.style.left = Math.random() * 100 + 'vw';
  const dur = 8 + Math.random() * 10;
  el.style.animationDuration = `${dur}s`;
  $('#emoji-sky').append(el);
  setTimeout(() => el.remove(), dur * 1000);
}
setInterval(spawnEmoji, 400);

/* ----------------------------------------------------
   4. Fade‑in met IntersectionObserver
---------------------------------------------------- */
const sectionObserver = new IntersectionObserver(
  ents => ents.forEach(e => e.isIntersecting && e.target.classList.add('visible')),
  { threshold: 0.2 }
);
$$('.section').forEach(sec => sectionObserver.observe(sec));

/* ----------------------------------------------------
   5. GSAP slide‑ins + parallax
---------------------------------------------------- */
if (typeof ScrollTrigger !== 'undefined' && typeof gsap !== 'undefined') {
  gsap.registerPlugin(ScrollTrigger);

  gsap.utils.toArray('.slide-in').forEach(card => {
    gsap.from(card, {
      y: 60,
      opacity: 0,
      scale: 0.93,
      duration: 0.6,
      delay: parseFloat(card.dataset.delay) || 0,
      ease: 'power2.out',
      scrollTrigger: { trigger: card, start: 'top 80%' }
    });
  });

  // Parallax‑achtergrond
  gsap.to('.parallax', {
    backgroundPositionY: '40%',
    ease: 'none',
    scrollTrigger: { trigger: '.parallax', scrub: true }
  });
}

/* ----------------------------------------------------
   6. VanillaTilt + flip‑kaarten
---------------------------------------------------- */
window.addEventListener('DOMContentLoaded', () => {
  // Tilt‑effect
  if (typeof VanillaTilt !== 'undefined') {
    VanillaTilt.init(document.querySelectorAll('.tilt'), {
      max: 8,
      speed: 400,
      glare: true,
      'max-glare': 0.25
    });
  }

  // Flip op tap/click
  $$('.service-card.flip').forEach(card =>
    card.addEventListener('click', () => card.classList.toggle('flipped'))
  );
});

/* ----------------------------------------------------
   7. Confetti op CTA‑buttons
---------------------------------------------------- */
import(
  'https://cdn.jsdelivr.net/npm/canvas-confetti@1.6.0/dist/confetti.browser.min.js'
).then(({ default: confetti }) => {
  $$('.js-confetti').forEach(btn =>
    btn.addEventListener('click', () =>
      confetti({ spread: 90, particleCount: 120, origin: { y: 0.6 } })
    )
  );
});
