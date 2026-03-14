"use client";

import { useRef } from "react";

export interface AttachedFile {
  name: string;
  blobUrl: string;
  contentType: string;
  uploading?: boolean;
  error?: string;
  file?: File;
}

interface Props {
  attachments: AttachedFile[];
  onAdd: (file: File) => void;
  onRemove: (name: string) => void;
}

export function AttachmentBar({ attachments, onAdd, onRemove }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <div style={{
      display: "flex", alignItems: "center", flexWrap: "wrap",
      gap: 8, padding: "6px 16px", minHeight: 40,
    }}>
      {/* Paperclip button */}
      <button
        title="Attach a .txt file"
        onClick={() => inputRef.current?.click()}
        style={{
          background: "none", border: "1px solid #ffffff22",
          borderRadius: 8, padding: "5px 10px",
          cursor: "pointer", color: "#9098b0", fontSize: 16,
          display: "flex", alignItems: "center", gap: 4,
          transition: "border-color 0.2s, color 0.2s",
        }}
        onMouseEnter={e => {
          (e.currentTarget as HTMLButtonElement).style.borderColor = "#3d5afe";
          (e.currentTarget as HTMLButtonElement).style.color = "#3d5afe";
        }}
        onMouseLeave={e => {
          (e.currentTarget as HTMLButtonElement).style.borderColor = "#ffffff22";
          (e.currentTarget as HTMLButtonElement).style.color = "#9098b0";
        }}
      >
        📎 <span style={{ fontSize: 12 }}>Attach</span>
      </button>

      <input
        ref={inputRef}
        type="file"
        accept=".txt,text/plain"
        style={{ display: "none" }}
        onChange={e => {
          const f = e.target.files?.[0];
          if (f) onAdd(f);
          e.target.value = ""; // reset so same file can be re-selected
        }}
      />

      {/* Attachment chips */}
      {attachments.map(att => (
        <div key={att.name} style={{
          display: "flex", alignItems: "center", gap: 6,
          background: att.error ? "#ef444418" : att.uploading ? "#3d5afe18" : "#22c55e18",
          border: `1px solid ${att.error ? "#ef444444" : att.uploading ? "#3d5afe44" : "#22c55e44"}`,
          borderRadius: 8, padding: "4px 10px",
          fontSize: 12, color: "#e8eaf0", maxWidth: 220,
        }}>
          <span style={{ fontSize: 14 }}>
            {att.uploading ? "⏳" : att.error ? "❌" : "📄"}
          </span>
          <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {att.name}
          </span>
          {att.uploading && (
            <span style={{ fontSize: 10, color: "#3d5afe", marginLeft: 2 }}>uploading…</span>
          )}
          {att.error && (
            <span style={{ fontSize: 10, color: "#ef4444", marginLeft: 2 }}>{att.error}</span>
          )}
          {!att.uploading && (
            <button
              onClick={() => onRemove(att.name)}
              style={{
                background: "none", border: "none", cursor: "pointer",
                color: "#5a6280", fontSize: 14, padding: 0, marginLeft: 2, lineHeight: 1,
              }}
              title="Remove attachment"
            >
              ×
            </button>
          )}
        </div>
      ))}
    </div>
  );
}
