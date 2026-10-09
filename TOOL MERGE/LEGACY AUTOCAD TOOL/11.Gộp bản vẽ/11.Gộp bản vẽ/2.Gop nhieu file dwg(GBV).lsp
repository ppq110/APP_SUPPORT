(defun c:GBV ( / mode pathname filelist p xht yht i oldos spacing
                p1 p2 blname fullpath ext lastEnt width height base-x base-y first-p1 bbox-width bbox-height)

  (vl-load-com)
;; Nhập khoảng cách giữa các bản vẽ
  (setq spacing (getreal "\nNhap khoang cach giua cac ban ve: "))

  (if (null spacing) (setq spacing 1000.0)) ; Dự phòng nếu không nhập
  

  ;; Hỏi hướng ghép
  (initget "Ngang Doc")
  (setq mode (getkword "\nBan muon ghep [Ngang/Doc]? <Ngang>: "))
  (if (null mode) (setq mode "Ngang"))

  ;; Chon thu muc
  (setq pathname (vl-filename-directory
                   (getfiled "Chon 1 file bat ky trong thu muc chua DWG" "" "dwg" 0)))

  ;; Lay danh sach DWG
  (setq filelist (vl-sort (vl-directory-files pathname "*.dwg" 1) '<))

  (if (null filelist)
    (progn (prompt "\nKhong tim thay file DWG.") (exit)))

  ;; Diem chen dau
  (setq p   (getpoint "\nChon diem chen dau tien: ")
        base-x (car p)
        base-y (cadr p)
        i   0
        oldos (getvar "osmode"))

  (setvar "osmode" 0)

  ;; Lap tung file
  (foreach filename filelist
    (setq fullpath (strcat pathname "\\" filename))

    ;; Chen file tai 0,0
    (command "-INSERT" fullpath "0,0" 1.0 1.0 0.0)
    (setq lastEnt (entlast))

    (if (and lastEnt (= (cdr (assoc 0 (entget lastEnt))) "INSERT"))
      (progn
        ;; Lay bounding box
        (vla-getboundingbox (vlax-ename->vla-object lastEnt) 'p1 'p2)
        (setq p1 (vlax-safearray->list p1)
              p2 (vlax-safearray->list p2)
              bbox-width  (- (car p2) (car p1))
              bbox-height (- (cadr p2) (cadr p1))
              blname (cdr (assoc 2 (entget lastEnt))))

        ;; Tinh vi tri chuyen doi
        (setq dx (- base-x (car p1))
              dy (- base-y (cadr p1)))
        (command "MOVE" lastEnt "" '(0 0) (list dx dy))

        ;; Cap nhat diem base-x, base-y
        (cond
          ((= (strcase mode) "NGANG") (setq base-x (+ base-x bbox-width spacing)))
          ((= (strcase mode) "DOC")   (setq base-y (- base-y (+ bbox-height spacing)))))

        ;; Explode & Purge
        (command "_.EXPLODE" lastEnt)
        (command "_.PURGE" "_B" blname "_N")
      )
      (prompt (strcat "\nKhong the chen file: " filename))
    )
  )

  (setvar "osmode" oldos)
  (princ)
)
