import React, { useState, useMemo, useEffect } from 'react';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/Button';
import { LoadingSkeleton } from '@/components/ui/LoadingSkeleton';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { DropdownSelect } from '@/components/ui/DropdownMenu';
import { type ProblemDetails } from '@/lib/api/problemDetails';
import { registerMemoryWipeListener } from '@/lib/queryClient';
import {
  Search,
  ChevronDown,
  ChevronUp,
  ChevronsUpDown,
  ChevronLeft,
  ChevronRight,
  ShieldCheck,
} from 'lucide-react';

export interface ColumnDef<T> {
  key: keyof T | string;
  header: string;
  sortable?: boolean;
  className?: string;
  render?: (item: T, index: number) => React.ReactNode;
}

export interface DataTableProps<T> {
  data: T[];
  columns: ColumnDef<T>[];
  dataScopeBadge?: string;
  searchPlaceholder?: string;
  isLoading?: boolean;
  error?: ProblemDetails | null;
  onRetry?: () => void;
  pageSize?: number;
  emptyTitle?: string;
  emptyDescription?: string;
  className?: string;
  onRowClick?: (item: T) => void;
}

export function DataTable<T extends object>({
  data,
  columns,
  dataScopeBadge,
  searchPlaceholder = 'Cari data...',
  isLoading = false,
  error = null,
  onRetry,
  pageSize = 10,
  emptyTitle = 'Tidak Ada Data',
  emptyDescription = 'Belum ada data yang tercatat dalam sistem.',
  className,
  onRowClick,
}: DataTableProps<T>) {
  const [searchTerm, setSearchTerm] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [currentPage, setCurrentPage] = useState(1);
  const [rowsPerPage, setRowsPerPage] = useState(pageSize);

  useEffect(() => {
    return registerMemoryWipeListener(() => {
      setSearchTerm('');
      setDebouncedSearch('');
      setCurrentPage(1);
    });
  }, []);

  // Debounce search input (300ms)
  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedSearch(searchTerm);
      setCurrentPage(1);
    }, 300);
    return () => clearTimeout(handler);
  }, [searchTerm]);

  // Filter
  const filteredData = useMemo(() => {
    if (!debouncedSearch.trim()) return data;
    const term = debouncedSearch.toLowerCase();
    return data.filter((item) =>
      Object.values(item as Record<string, unknown>).some((val) =>
        String(val ?? '').toLowerCase().includes(term)
      )
    );
  }, [data, debouncedSearch]);

  // Sort
  const sortedData = useMemo(() => {
    if (!sortKey) return filteredData;
    return [...filteredData].sort((a, b) => {
      const valA = (a as Record<string, unknown>)[sortKey];
      const valB = (b as Record<string, unknown>)[sortKey];

      if (valA === valB) return 0;
      if (valA === null || valA === undefined) return 1;
      if (valB === null || valB === undefined) return -1;

      const comp = String(valA).localeCompare(String(valB), undefined, {
        numeric: true,
        sensitivity: 'base',
      });
      return sortDir === 'asc' ? comp : -comp;
    });
  }, [filteredData, sortKey, sortDir]);

  // Paginate
  const totalItems = sortedData.length;
  const totalPages = Math.max(1, Math.ceil(totalItems / rowsPerPage));
  const startIndex = (currentPage - 1) * rowsPerPage;
  const paginatedData = sortedData.slice(startIndex, startIndex + rowsPerPage);

  const handleSort = (key: string) => {
    if (sortKey === key) {
      if (sortDir === 'asc') {
        setSortDir('desc');
      } else {
        setSortKey(null);
        setSortDir('asc');
      }
    } else {
      setSortKey(key);
      setSortDir('asc');
    }
  };

  if (error) {
    return <ErrorState problem={error} onRetry={onRetry} className={className} />;
  }

  return (
    <div className={cn('w-full flex flex-col space-y-4', className)}>
      {/* Top Bar: Search, Data Scope, Controls */}
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
        {/* Search Input */}
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 h-4 w-4 text-text-secondary" />
          <input
            type="text"
            autoComplete="off"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            placeholder={searchPlaceholder}
            className="w-full pl-10 pr-4 py-2.5 text-sm rounded-xl border border-interactive bg-white text-text-primary placeholder:text-text-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-navy-action min-h-[44px]"
            aria-label="Cari data tabel"
          />
        </div>

        {/* Data Scope Indicator */}
        {dataScopeBadge && (
          <div className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-[#EEF2F6] text-[#0D1B39] text-xs font-semibold border border-[#D0D7E2] select-none self-start sm:self-center">
            <ShieldCheck className="h-3.5 w-3.5 text-amber-text shrink-0" aria-hidden="true" />
            <span>{dataScopeBadge}</span>
          </div>
        )}
      </div>

      {/* Table Container */}
      <div className="overflow-x-auto rounded-2xl border border-[#ECEEF2] bg-white shadow-card-soft">
        <table className="w-full text-left text-sm tabular-nums border-collapse" role="table">
          <thead className="bg-[#F6F7F9] text-text-secondary text-xs uppercase font-semibold border-b border-[#ECEEF2]">
            <tr>
              {columns.map((col) => {
                const keyStr = String(col.key);
                const isSorted = sortKey === keyStr;

                return (
                  <th key={keyStr} scope="col" className={cn('p-4 py-3.5', col.className)}>
                    {col.sortable ? (
                      <button
                        type="button"
                        onClick={() => handleSort(keyStr)}
                        className="inline-flex items-center gap-1.5 hover:text-text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-navy-action rounded font-semibold min-h-[36px]"
                        aria-label={`Urutkan ${col.header} (${isSorted ? (sortDir === 'asc' ? 'naik' : 'turun') : 'tidak aktif'})`}
                      >
                        <span>{col.header}</span>
                        {isSorted ? (
                          sortDir === 'asc' ? (
                            <ChevronUp className="h-3.5 w-3.5 text-amber-text" />
                          ) : (
                            <ChevronDown className="h-3.5 w-3.5 text-amber-text" />
                          )
                        ) : (
                          <ChevronsUpDown className="h-3.5 w-3.5 opacity-40" />
                        )}
                      </button>
                    ) : (
                      <span>{col.header}</span>
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>

          <tbody className="divide-y divide-[#ECEEF2]">
            {isLoading ? (
              // Loading Skeleton State
              Array.from({ length: 5 }).map((_, i) => (
                <tr key={i} role="row">
                  <td colSpan={columns.length} className="p-4">
                    <LoadingSkeleton height="28px" width="100%" />
                  </td>
                </tr>
              ))
            ) : paginatedData.length === 0 ? (
              // Empty State
              <tr>
                <td colSpan={columns.length} className="p-8 text-center">
                  <EmptyState
                    variant={debouncedSearch ? 'filter-empty' : 'tenant-empty'}
                    title={debouncedSearch ? 'Tidak Ada Hasil yang Cocok' : emptyTitle}
                    description={
                      debouncedSearch
                        ? `Tidak ada data yang cocok dengan kata kunci "${debouncedSearch}".`
                        : emptyDescription
                    }
                  />
                </td>
              </tr>
            ) : (
              // Data Rows
              paginatedData.map((row, rowIndex) => (
                <tr
                  key={rowIndex}
                  role="row"
                  onClick={() => onRowClick?.(row)}
                  className={cn(
                    'hover:bg-surface-soft/60 transition-colors',
                    onRowClick && 'cursor-pointer'
                  )}
                >
                  {columns.map((col) => {
                    const keyStr = String(col.key);
                    return (
                      <td key={keyStr} className={cn('p-4 text-text-primary', col.className)}>
                        {col.render
                          ? col.render(row, rowIndex)
                          : String((row as Record<string, unknown>)[keyStr] ?? '-')}
                      </td>
                    );
                  })}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      {!isLoading && sortedData.length > 0 && (
        <div className="flex flex-col sm:flex-row items-center justify-between gap-4 text-xs text-text-secondary px-1">
          <div className="flex items-center gap-2">
            <span>Tampilkan</span>
            <DropdownSelect<number>
              value={rowsPerPage}
              onChange={(size) => {
                setRowsPerPage(size);
                setCurrentPage(1);
              }}
              ariaLabel="Pilih jumlah baris per halaman"
              buttonClassName="min-h-[36px] px-3 py-1.5 text-xs sm:text-[13px]"
              panelClassName="min-w-[136px]"
              position="top"
              align="left"
              options={[10, 25, 50, 100].map((size) => ({
                value: size,
                label: String(size),
              }))}
            />
            <span>dari {totalItems} baris</span>
          </div>

          <div className="flex items-center gap-2">
            <span>
              Halaman {currentPage} dari {totalPages}
            </span>
            <div className="flex items-center gap-1">
              <Button
                variant="outline"
                size="sm"
                disabled={currentPage <= 1}
                onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                aria-label="Halaman sebelumnya"
                className="min-h-[36px] min-w-[36px] p-1.5"
              >
                <ChevronLeft className="h-4 w-4" />
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={currentPage >= totalPages}
                onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
                aria-label="Halaman berikutnya"
                className="min-h-[36px] min-w-[36px] p-1.5"
              >
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
