(function () {
  var en = {
    nav_features: "Features", nav_safety: "Safety", nav_faq: "FAQ", nav_download: "Download",
    eyebrow: "Free · Windows 10 / 11",
    h1: "A faster, cleaner PC in one click.",
    lead: "Ultimate PC Cleaner removes junk, speeds up startup and checks your PC's health – safely, with confirmation before every action.",
    download: "Download for Windows", source: "Source code",
    meta: "Installer · ~60 MB · 64-bit · .NET not required", freed: "Freed",
    feat_h: "Everything you need in one place",
    f1_t: "Cleaning", f1_d: "Temp files, Windows Update cache, error reports, Recycle Bin, browser caches (Edge, Chrome, Brave, Firefox) and DirectX shader cache.",
    f2_t: "1-Click Boost", f2_d: "Cleans selected junk, flushes DNS and frees RAM – after you confirm.",
    f3_t: "Startup apps", f3_d: "Enable or disable programs that start with Windows. Nothing is deleted.",
    f4_t: "Security scan", f4_d: "Front-end for Microsoft Defender, AMSI script check and suspicious-item detection, with a restorable quarantine.",
    f5_t: "Optimize", f5_d: "Power plan, visual effects and Game DVR – all reversible, with a restore point.",
    f6_t: "System health", f6_d: "SFC, DISM, drive optimization and CHKDSK with live output.",
    langs: "Available in Serbian, English, Russian, German and Chinese.",
    safe_h: "Built with care", safe_lead: "A cleaner must never break anything. Every action is verified and under your control.",
    s1: "Deletes files only – never whole system folders.", s2: "Every file is re-checked right before deletion.",
    s3: "Browser cache only: cookies, history and passwords are untouched.", s4: "Confirmation before every destructive action.",
    s5: "Restore point before tweaks; everything can be reverted.", s6: "No account, no ads, no tracking.",
    dl_h: "Download for free", dl_lead: "Windows 10 (1809+) and Windows 11, 64-bit. Run the downloaded installer and follow the wizard.",
    all_rel: "All versions and release notes",
    smart_t: "Windows SmartScreen warning?",
    smart_d: "The app isn't code-signed yet, so Windows may show \"Windows protected your PC\". Click \"More info\" → \"Run anyway\". The source code is public, so you can check what it does.",
    faq_h: "FAQ",
    q1: "Is it really free?", a1: "Yes. No subscription, no hidden costs, no ads.",
    q2: "Does it need administrator rights?", a2: "Not for basic cleaning. Windows Temp, Prefetch, system health and Defender scans need administrator; the app explains this and offers a restart only when you ask.",
    q3: "Will it speed up my PC?", a3: "It frees space and reduces startup load. No miracles – e.g. clearing Prefetch doesn't speed anything up, so it's off by default.",
    q4: "How do I uninstall it?", a4: "Via Windows Settings → Apps. Settings and logs are in <code>%LOCALAPPDATA%\\UltimatePCCleaner</code>.",
    report: "Report an issue"
  };
  var sr = {}, lang = "sr";
  document.querySelectorAll("[data-i18n]").forEach(function (el) { sr[el.dataset.i18n] = el.innerHTML; });

  function apply(l) {
    lang = l;
    document.documentElement.lang = l;
    document.querySelectorAll("[data-i18n]").forEach(function (el) {
      var v = (l === "en" ? en : sr)[el.dataset.i18n];
      if (v != null) el.innerHTML = v;
    });
    document.getElementById("lang").textContent = l === "en" ? "SR" : "EN";
    try { localStorage.setItem("lang", l); } catch (e) {}
  }
  document.getElementById("lang").addEventListener("click", function () { apply(lang === "en" ? "sr" : "en"); });
  try { if (localStorage.getItem("lang") === "en") apply("en"); } catch (e) {}

  var els = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window) {
    var io = new IntersectionObserver(function (es) {
      es.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } });
    }, { threshold: 0.12 });
    els.forEach(function (el) { io.observe(el); });
  } else { els.forEach(function (el) { el.classList.add("in"); }); }
})();
