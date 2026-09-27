import React from 'react';

interface LogoProps {
  size?: number;
  className?: string;
}

export const CodeCompassLogo: React.FC<LogoProps> = ({ size = 36, className = '' }) => {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 40 40"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      style={{ flexShrink: 0, display: 'block' }}
    >
      <defs>
        {/* Background rounded squircle gradient */}
        <linearGradient id="cc-squircle-bg" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#1e293b" />
          <stop offset="45%" stopColor="#0f172a" />
          <stop offset="100%" stopColor="#1e1b4b" />
        </linearGradient>

        {/* Outer glowing border gradient */}
        <linearGradient id="cc-border-grad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#38bdf8" stopOpacity="0.8" />
          <stop offset="50%" stopColor="#818cf8" stopOpacity="0.4" />
          <stop offset="100%" stopColor="#2563eb" stopOpacity="0.9" />
        </linearGradient>

        {/* Compass needle gradients */}
        <linearGradient id="cc-north-bright" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#38bdf8" />
          <stop offset="100%" stopColor="#2563eb" />
        </linearGradient>

        <linearGradient id="cc-north-shadow" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#60a5fa" />
          <stop offset="100%" stopColor="#1d4ed8" />
        </linearGradient>

        <linearGradient id="cc-south-bright" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#a78bfa" />
          <stop offset="100%" stopColor="#6366f1" />
        </linearGradient>

        <linearGradient id="cc-south-shadow" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#818cf8" />
          <stop offset="100%" stopColor="#4338ca" />
        </linearGradient>

        {/* Center glowing core */}
        <radialGradient id="cc-core-glow" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#38bdf8" stopOpacity="1" />
          <stop offset="60%" stopColor="#0284c7" stopOpacity="0.8" />
          <stop offset="100%" stopColor="#0369a1" stopOpacity="0" />
        </radialGradient>

        {/* Drop shadow / glow */}
        <filter id="cc-needle-glow" x="-20%" y="-20%" width="140%" height="140%">
          <feDropShadow dx="0" dy="1" stdDeviation="2" floodColor="#38bdf8" floodOpacity="0.4" />
        </filter>
      </defs>

      {/* Squircle Badge Background */}
      <rect width="40" height="40" rx="9" fill="url(#cc-squircle-bg)" />
      <rect x="0.75" y="0.75" width="38.5" height="38.5" rx="8.25" stroke="url(#cc-border-grad)" strokeWidth="1.2" />

      {/* Subtle Navigation Dial Grid / Ring */}
      <circle cx="20" cy="20" r="13.5" stroke="rgba(255, 255, 255, 0.08)" strokeWidth="1" />
      <circle cx="20" cy="20" r="13.5" stroke="url(#cc-border-grad)" strokeWidth="1" strokeDasharray="2 3" opacity="0.6" />

      {/* Cardinal Axis Markers */}
      <line x1="20" y1="4.5" x2="20" y2="7.5" stroke="#38bdf8" strokeWidth="1.6" strokeLinecap="round" />
      <line x1="20" y1="32.5" x2="20" y2="35.5" stroke="#818cf8" strokeWidth="1.6" strokeLinecap="round" />
      <line x1="4.5" y1="20" x2="7.5" y2="20" stroke="#64748b" strokeWidth="1.4" strokeLinecap="round" />
      <line x1="32.5" y1="20" x2="35.5" y2="20" stroke="#64748b" strokeWidth="1.4" strokeLinecap="round" />

      {/* 4-Point Faceted Compass Needle */}
      <g filter="url(#cc-needle-glow)">
        {/* North Arrow - Left Facet */}
        <polygon points="20,6.5 20,20 15,20" fill="url(#cc-north-shadow)" />
        {/* North Arrow - Right Facet (Bright) */}
        <polygon points="20,6.5 25,20 20,20" fill="url(#cc-north-bright)" />

        {/* South Arrow - Left Facet */}
        <polygon points="20,33.5 15,20 20,20" fill="url(#cc-south-shadow)" />
        {/* South Arrow - Right Facet */}
        <polygon points="20,33.5 20,20 25,20" fill="url(#cc-south-bright)" />

        {/* East Arrow Point */}
        <polygon points="33.5,20 20,16.5 20,23.5" fill="#94a3b8" opacity="0.6" />
        {/* West Arrow Point */}
        <polygon points="6.5,20 20,23.5 20,16.5" fill="#cbd5e1" opacity="0.8" />
      </g>

      {/* Center Pivot Ring & Core */}
      <circle cx="20" cy="20" r="4.2" fill="#0f172a" stroke="#38bdf8" strokeWidth="1.5" />
      <circle cx="20" cy="20" r="2.5" fill="url(#cc-core-glow)" />
      <circle cx="20" cy="20" r="1.2" fill="#ffffff" />
    </svg>
  );
};
